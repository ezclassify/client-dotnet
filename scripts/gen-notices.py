#!/usr/bin/env python3
"""Third-party license gate and THIRD-PARTY-NOTICES.md generator.

Computes the production dependency closure of the published (packable)
projects via `dotnet list <project> package --include-transitive`, resolves
each package's license from its nuspec in the local NuGet packages folder,
and either:

  check            fail if any package's license is unknown or not allowlisted
  notices          rewrite THIRD-PARTY-NOTICES.md from the current closure
  notices --check  fail if THIRD-PARTY-NOTICES.md is stale (CI mode)

Run from the repository root, after a `dotnet restore` of the solution.
The NuGet packages folder is taken from $NUGET_PACKAGES when set, else
~/.nuget/packages.
"""

import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
NOTICES_FILE = REPO_ROOT / "THIRD-PARTY-NOTICES.md"

# Licenses acceptable in the shipped runtime closure (SPDX expressions).
# A permissive allowlist. Anything outside this set
# fails `check` and needs an explicit review, not a silent addition here.
ALLOWED_LICENSES = {
    "MIT",
    "ISC",
    "Apache-2.0",
    "BSD-2-Clause",
    "BSD-3-Clause",
    "0BSD",
}

# Legacy packages declare their license via `licenseUrl` instead of an SPDX
# expression. Map the known documents to what they actually contain. Every
# URL added here must be verified by reading the linked document.
LICENSE_URL_MAP = {
    "https://github.com/dotnet/corefx/blob/master/license.txt": "MIT",
    "https://github.com/dotnet/standard/blob/master/license.txt": "MIT",
    "https://licenses.nuget.org/mit": "MIT",
    # MICROSOFT SOFTWARE LICENSE TERMS: MICROSOFT .NET LIBRARY. Proprietary
    # but redistributable. Only acceptable for build-time-only packages; the
    # gate below rejects it anywhere in the shipped runtime closure.
    "http://go.microsoft.com/fwlink/?linkid=329770": "Microsoft .NET Library License",
}

# Packages that appear in `dotnet list --include-transitive` output for the
# netstandard2.0 targets but are compile-time targeting-pack references, not
# shipped dependencies: NETStandard.Library is auto-referenced by the SDK and
# Microsoft.NETCore.Platforms is reachable only through it. Verified against
# the packed nuspec: `dotnet pack EzClassify.Rest.Contract` declares only
# System.Text.Json as a dependency. If a new dependency path ever pulls one
# of these at runtime, the packed nuspec (and this claim) must be re-checked.
BUILD_TIME_ONLY = {"netstandard.library", "microsoft.netcore.platforms"}

# Licenses tolerated for BUILD_TIME_ONLY packages on top of ALLOWED_LICENSES.
BUILD_TIME_ALLOWED = {"Microsoft .NET Library License"}


def packable_projects():
    """Top-level project files that produce published NuGet packages."""
    projects = []
    for csproj in sorted(REPO_ROOT.glob("*/*.csproj")):
        text = csproj.read_text()
        if re.search(r"<IsPackable>\s*false\s*</IsPackable>", text, re.I):
            continue
        projects.append(csproj)
    return projects


def run_dotnet_list(csproj):
    """Parsed JSON from `dotnet list <csproj> package --include-transitive`."""
    # Repo-relative path so the command works from any checkout location,
    # including a container that mounts the repository root as its working dir.
    out = subprocess.run(
        [
            "dotnet",
            "list",
            str(csproj.relative_to(REPO_ROOT)),
            "package",
            "--include-transitive",
            "--format",
            "json",
        ],
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
        text=True,
    ).stdout
    # The dotnet CLI can print a workload notice before the JSON document.
    return json.loads(out[out.index("{"):])


def collect_closure(projects):
    """Map of (package id, version) -> set of consuming published projects.

    Auto-referenced packages (the netstandard2.0 targeting pack) are kept so
    they can be reported in the build-time section, but their autoReferenced
    flag never reaches the transitive entries, which is why BUILD_TIME_ONLY
    exists as an explicit list.
    """
    closure = {}
    for csproj in projects:
        name = csproj.stem
        data = run_dotnet_list(csproj)
        for project in data["projects"]:
            for framework in project.get("frameworks", []):
                packages = framework.get("topLevelPackages", []) + framework.get(
                    "transitivePackages", []
                )
                for pkg in packages:
                    key = (pkg["id"], pkg["resolvedVersion"])
                    closure.setdefault(key, set()).add(name)
    return closure


def nuspec_metadata(pkg_id, version):
    """(license, copyright, project URL) from the cached nuspec, or an error."""
    packages_dir = Path(os.environ.get("NUGET_PACKAGES") or Path.home() / ".nuget" / "packages")
    nuspec = packages_dir / pkg_id.lower() / version.lower() / f"{pkg_id.lower()}.nuspec"
    if not nuspec.is_file():
        return None, None, None, f"nuspec not found at {nuspec} (run dotnet restore first)"
    root = ET.parse(nuspec).getroot()
    ns = {"n": root.tag.split("}")[0].strip("{")} if root.tag.startswith("{") else {}
    prefix = "n:" if ns else ""
    meta = root.find(f"{prefix}metadata", ns)

    def field(tag):
        el = meta.find(f"{prefix}{tag}", ns)
        return el.text.strip() if el is not None and el.text else ""

    license_el = meta.find(f"{prefix}license", ns)
    if license_el is not None and license_el.get("type") == "expression":
        license_name = license_el.text.strip()
    else:
        url = field("licenseUrl")
        license_name = LICENSE_URL_MAP.get(url.lower())
        if license_name is None:
            return None, None, None, f"unmapped license (licenseUrl: {url or 'none'})"
    return license_name, field("copyright"), field("projectUrl"), None


def direct_dependencies(csproj):
    """(first-party project names, third-party package ids) declared directly."""
    root = ET.parse(csproj).getroot()
    project_refs = sorted(
        Path(el.get("Include").replace("\\", "/")).stem
        for el in root.iter("ProjectReference")
    )
    package_refs = sorted(el.get("Include") for el in root.iter("PackageReference"))
    return project_refs, package_refs


def build_inventory():
    """Resolve the full closure with license metadata, split runtime/build-time."""
    projects = packable_projects()
    closure = collect_closure(projects)
    runtime, build_time, errors = [], [], []
    for (pkg_id, version), users in sorted(closure.items()):
        license_name, copyright_, project_url, error = nuspec_metadata(pkg_id, version)
        if error:
            errors.append(f"{pkg_id} {version}: {error}")
            continue
        entry = {
            "id": pkg_id,
            "version": version,
            "license": license_name,
            "copyright": copyright_,
            "projectUrl": project_url,
            "usedBy": sorted(users),
        }
        (build_time if pkg_id.lower() in BUILD_TIME_ONLY else runtime).append(entry)
    return projects, runtime, build_time, errors


def check(projects, runtime, build_time, errors):
    failures = list(errors)
    for entry in runtime:
        if entry["license"] not in ALLOWED_LICENSES:
            failures.append(
                f"{entry['id']} {entry['version']}: license '{entry['license']}' "
                "is not in the runtime allowlist"
            )
    for entry in build_time:
        if entry["license"] not in ALLOWED_LICENSES | BUILD_TIME_ALLOWED:
            failures.append(
                f"{entry['id']} {entry['version']}: license '{entry['license']}' "
                "is not allowed even for build-time packages"
            )
        elif entry["license"] not in ALLOWED_LICENSES:
            print(
                f"note: {entry['id']} {entry['version']} uses '{entry['license']}' "
                "(build-time targeting pack only, not shipped)"
            )
    checked = len(runtime) + len(build_time)
    if failures:
        print(f"License check FAILED ({len(failures)} problem(s), {checked} package(s)):")
        for failure in failures:
            print(f"  - {failure}")
        return 1
    print(
        f"License check passed: {len(runtime)} runtime + {len(build_time)} build-time "
        f"package(s) across {len(projects)} published project(s)."
    )
    return 0


def render_notices(projects, runtime, build_time):
    lines = [
        "# Third-Party Notices",
        "",
        "<!-- Generated by scripts/gen-notices.py. Do not hand-edit. -->",
        "<!-- Refresh with: python3 scripts/gen-notices.py notices -->",
        "",
        "The EzClassify .NET SDK is distributed under the Apache License 2.0 (see `LICENSE`).",
        "",
        "This file lists the third-party runtime (production) dependencies of the",
        "published NuGet packages in this repository, together with their licenses and",
        "copyright notices. Test-only and source-generation dependencies are not listed",
        "because they are not part of the distributed runtime.",
        "",
        "## Published packages",
        "",
        "| Package | Direct runtime dependencies |",
        "| --- | --- |",
    ]
    for csproj in projects:
        project_refs, package_refs = direct_dependencies(csproj)
        deps = [f"`{name}` (first-party)" for name in project_refs]
        deps += [f"`{pkg}`" for pkg in package_refs]
        lines.append(f"| `{csproj.stem}` | {', '.join(deps) if deps else 'none'} |")
    lines += [
        "",
        "## Third-party runtime dependencies",
        "",
        "The following non-first-party packages are pulled in, directly or",
        "transitively, by the published packages above.",
        "",
    ]
    for entry in runtime:
        lines += [
            f"### {entry['id']} {entry['version']}",
            "",
            f"- License: {entry['license']}",
            f"- Copyright: {entry['copyright'] or '(not declared)'}",
            f"- Project: {entry['projectUrl'] or '(not declared)'}",
            f"- Used by: {', '.join(f'`{name}`' for name in entry['usedBy'])}",
            "",
        ]
    lines += [
        "## Build-time targeting pack packages",
        "",
        "The netstandard2.0 projects compile against the `NETStandard.Library`",
        "targeting pack, which the SDK references automatically at build time. These",
        "packages never appear as dependencies of the published packages (the packed",
        "nuspec declares only the runtime dependencies above) and ship no code in the",
        "SDK, but they are listed here for completeness because dependency scanners",
        "report them in the restore closure.",
        "",
        "| Package | Version | License | Copyright |",
        "| --- | --- | --- | --- |",
    ]
    for entry in build_time:
        lines.append(
            f"| {entry['id']} | {entry['version']} | {entry['license']} | "
            f"{entry['copyright'] or '(not declared)'} |"
        )
    lines += [
        "",
        "`Microsoft.NETCore.Platforms` declares the legacy Microsoft .NET Library",
        "license terms via its `licenseUrl` field. It is acceptable here only because",
        "it is a compile-time targeting-pack reference; the license gate in",
        "`scripts/gen-notices.py` rejects it anywhere in the shipped runtime closure.",
        "",
        "## MIT License",
        "",
        "```",
        MIT_TEXT,
        "```",
        "",
    ]
    return "\n".join(lines)


MIT_TEXT = """MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE."""


def main():
    args = sys.argv[1:]
    if not args or args[0] not in ("check", "notices"):
        print(__doc__)
        return 2
    projects, runtime, build_time, errors = build_inventory()
    if args[0] == "check":
        return check(projects, runtime, build_time, errors)
    if errors:
        print("Cannot generate notices; unresolved packages:")
        for error in errors:
            print(f"  - {error}")
        return 1
    content = render_notices(projects, runtime, build_time)
    if "--check" in args[1:]:
        current = NOTICES_FILE.read_text() if NOTICES_FILE.is_file() else ""
        if current != content:
            print(
                "THIRD-PARTY-NOTICES.md is stale. Regenerate it with:\n"
                "  python3 scripts/gen-notices.py notices\n"
                "and commit the result."
            )
            return 1
        print("THIRD-PARTY-NOTICES.md is current.")
        return 0
    NOTICES_FILE.write_text(content)
    print(f"Wrote {NOTICES_FILE.relative_to(REPO_ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
