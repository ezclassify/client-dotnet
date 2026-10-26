#!/usr/bin/env node

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'vendor/spec/MANIFEST.json'), 'utf8'));
const expectedGenerated = [
  'EzClassify.Rest.Contract/ContextEnvelopeModels.g.cs',
  'EzClassify.Rest.Contract/ContextModels.g.cs',
  'EzClassify.Rest.Contract/DocumentAssessmentModels.g.cs',
  'EzClassify.Rest.Contract/Models.g.cs',
  'EzClassify.Rest.Contract/WireEnums.g.cs',
];
const errors = [];

if (JSON.stringify(manifest.generatedPaths) !== JSON.stringify(expectedGenerated)) {
  errors.push('vendor/spec/MANIFEST.json must name exactly the five canonical generated C# outputs.');
}

const declarationPattern = /^\s*public\s+(?:(?:sealed|readonly)\s+)*(?:record(?:\s+(?:class|struct))?|enum|struct)\s+([A-Za-z_]\w*)/gm;
const classPattern = /^\s*public\s+(?:(?:sealed|static|abstract|partial|readonly)\s+)*(?:class|struct)\s+([A-Za-z_]\w*)/gm;
const owners = new Map();
for (const relative of expectedGenerated) {
  const absolute = path.join(root, relative);
  if (!fs.existsSync(absolute)) {
    errors.push(`Missing generated output: ${relative}`);
    continue;
  }
  const source = fs.readFileSync(absolute, 'utf8');
  if (!source.startsWith('// Code generated')) {
    errors.push(`Generated output lacks its ownership header: ${relative}`);
  }
  for (const match of source.matchAll(declarationPattern)) {
    const paths = owners.get(match[1]) ?? [];
    paths.push(relative);
    owners.set(match[1], paths);
  }
}

for (const [name, paths] of owners) {
  if (paths.length !== 1) errors.push(`${name} has multiple generated declaration owners: ${paths.join(', ')}`);
}

function walk(directory, suffix) {
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (entry.name === 'bin' || entry.name === 'obj' || entry.name === '.git') continue;
    const absolute = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...walk(absolute, suffix));
    else if (entry.isFile() && entry.name.endsWith(suffix)) files.push(absolute);
  }
  return files;
}

const generatedSet = new Set(expectedGenerated);
for (const absolute of walk(root, '.cs')) {
  const relative = path.relative(root, absolute).split(path.sep).join('/');
  if (generatedSet.has(relative)) continue;
  const source = fs.readFileSync(absolute, 'utf8');
  const reportedShadows = new Set();
  for (const match of source.matchAll(declarationPattern)) {
    if (owners.has(match[1])) {
      errors.push(`Shadow declaration for generated ${match[1]} in ${relative}.`);
      reportedShadows.add(match[1]);
    }
    if (relative.startsWith('EzClassify.Rest.Contract/')) {
      errors.push(`Wire declaration ${match[1]} must be generator-owned, not declared in ${relative}.`);
    }
  }
  for (const match of source.matchAll(classPattern)) {
    if (owners.has(match[1]) && !reportedShadows.has(match[1])) {
      errors.push(`Shadow declaration for generated ${match[1]} in ${relative}.`);
    }
  }
}

for (const absolute of walk(root, '.csproj')) {
  const relative = path.relative(root, absolute).split(path.sep).join('/');
  const source = fs.readFileSync(absolute, 'utf8');
  if (/<PackageReference\b[^>]*\bVersion(?:Override)?\s*=/is.test(source)
      || /<PackageReference\b[^>]*>[\s\S]*?<Version(?:Override)?>/is.test(source)) {
    errors.push(`Package versions belong in Directory.Packages.props, not ${relative}.`);
  }
}

if (owners.size === 0) errors.push('No generated public record, enum, or struct declarations were found.');

if (errors.length > 0) {
  for (const error of errors) console.error(`ERROR: ${error}`);
  process.exit(1);
}

console.log(`Architecture guard passed: ${owners.size} generated declarations have one owner and project files contain no package versions.`);
