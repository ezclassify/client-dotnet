using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EzClassify;

/// <summary>
/// Scheme-domain operations bound to a connected Context. Reached via
/// <see cref="EzClassifyClient.Scheme"/>. Every call routes through
/// <c>/v1/ctx/{ctxId}/{version}/scheme/*</c>; the Context is selected by the
/// path, so request bodies carry payload only.
/// </summary>
public sealed class ContextSchemeOps
{
    private readonly EzClassifyClient _client;

    internal ContextSchemeOps(EzClassifyClient client) => _client = client;

    /// <summary>
    /// Process a marking for someone who is writing it: the author's call.
    ///
    /// <para><paramref name="input"/> is what was typed and
    /// <paramref name="assertions"/> the facts already chosen. The server
    /// settles the two together as one marking, which is what a composer sends
    /// on each keystroke, and either may be empty.</para>
    ///
    /// <para>The answer is everything <see cref="ValidateAsync(string, string?, string?, string?, IEnumerable{string}?, string?, CancellationToken)"/>
    /// answers, settled the same way, plus the corrections to the text, the fix
    /// paths the server has proven, a rendering for each output named in
    /// <paramref name="outputs"/> and, when <paramref name="suggestFor"/> names
    /// categories, their picker options. Nothing beyond that is written:
    /// <see cref="ProcessResult.Renders"/> is empty unless outputs are named,
    /// and <see cref="ProcessResult.SuggestionsFor"/> is null unless categories
    /// are. A reader that only needs the verdict calls validate; the trace of
    /// how the engine got here is <see cref="ExplainAsync"/>.</para>
    ///
    /// <para>The result carries the notices the scheme attaches to the asserted
    /// concepts, so a caller does not follow up with a separate notices
    /// call.</para>
    /// </summary>
    /// <param name="input">What was typed. Empty when <paramref name="assertions"/> carries the marking alone.</param>
    /// <param name="outputFormat">Optional output the returned marking is written in: a format id, or for a scheme that declares surfaces, a <c>surface/view</c> address or a view id. Omitted, the scheme's default format, or for a scheme that declares surfaces, each surface the result writes in its default for the marking's role. It selects output only and never narrows what may be read.</param>
    /// <param name="surface">Optional validation surface, named with one of the scheme's scope ids. Omitted, the marking is evaluated as a whole.</param>
    /// <param name="inputFormat">Optional name for what the text is read as: a format id, or for a scheme that declares surfaces, a view id, a <c>surface/view</c> address, or one of a view's forms. Omitted, recognition works it out and reports the view or format it read the text as in <see cref="RecognitionWithCorrections.InputFormat"/>.</param>
    /// <param name="assertions">Optional facts already chosen, settled together with <paramref name="input"/> as one marking.</param>
    /// <param name="contextJson">Optional processing context as a JSON object string, forwarded verbatim to the engine. <c>{"role":"part","partOf":{"rendered":"..."}}</c> marks the input as a portion of the given whole marking. Omitted, the input is treated as a whole marking.</param>
    /// <param name="parts">Part markings the whole must cover. The whole is judged as covering them: a part it does not cover is an error, and so is a part that does not read as a marking. Every proposed fix and every rank floor is worked out against the whole and these parts together, so a raise never lands below what a part needs. Each entry is a marking in one of the scheme's part formats, or any format the scheme reads. Empty or absent, the whole stands alone.</param>
    /// <param name="outputs">The outputs to write into <see cref="ProcessResult.Renders"/>, each spelled exactly as a key of it is: a <c>surface/view</c> address for a scheme that declares surfaces, a format id otherwise. Omitted or empty, it is empty. The server refuses an output the scheme does not declare.</param>
    /// <param name="suggestFor">Category ids to answer picker options for, in <see cref="ProcessResult.SuggestionsFor"/>. Omitted, nothing is suggested and that property is null. The server refuses a category the scheme does not declare.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the process result.</returns>
    /// <exception cref="ArgumentException"><paramref name="contextJson"/> is empty, is not valid JSON, or is not a JSON object.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeProcessResult> ProcessAsync(
        string input,
        string? outputFormat = null,
        string? surface = null,
        string? inputFormat = null,
        IEnumerable<Assertion>? assertions = null,
        string? contextJson = null,
        IEnumerable<string>? parts = null,
        IEnumerable<string>? outputs = null,
        IEnumerable<string>? suggestFor = null,
        CancellationToken cancellationToken = default)
    {
        using var contextDoc = CtxPayload.OptionalJsonObjectArgument(contextJson, nameof(contextJson));
        var body = CtxPayload.AuthorBody(input, outputFormat, surface, inputFormat, assertions, contextDoc, parts);
        CtxPayload.PutIfSet(body, "outputs", outputs);
        CtxPayload.PutIfSet(body, "suggestFor", suggestFor);
        return await _client.CachedPostCtxAsync("scheme.process", "scheme/process",
            body, ContextResultParser.ParseContextSchemeProcessResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Judge a marking for someone who is reading it: the reader's call.
    ///
    /// <para>Code that displays a marking, gates on it, stores it, or hands its
    /// facts to an access check or a rollup calls this. The server settles the
    /// marking exactly as <see cref="ProcessAsync"/> does and then judges it,
    /// so the verdicts mean the same here as on every other operation:
    /// <see cref="ValidateResult.Valid"/> says the marking stands once settled,
    /// and <see cref="ValidateResult.InputWasValid"/> that it was right as
    /// written. The answer carries the settled facts, the verdicts, errors and
    /// warnings, the changes settling made, the marking written back with its
    /// assessment, how recognition read it, the notices, and the color. Fix
    /// paths and corrections to the text are for an author, and come from
    /// <see cref="ProcessAsync"/>.</para>
    /// </summary>
    /// <param name="input">Marking text to judge.</param>
    /// <param name="surface">Optional validation surface, named with one of the scheme's scope ids. Omitted, the marking is evaluated as a whole.</param>
    /// <param name="outputFormat">Optional output the returned marking is written in: a format id, or for a scheme that declares surfaces, a <c>surface/view</c> address or a view id. Omitted, the scheme's default format, or for a scheme that declares surfaces, each surface the result writes in its default for the marking's role. It selects output only and never narrows what may be read.</param>
    /// <param name="inputFormat">Optional name for what the text is read as: a format id, or for a scheme that declares surfaces, a view id, a <c>surface/view</c> address, or one of a view's forms. Omitted, recognition works it out and reports the view or format it read the text as in <see cref="RecognitionResult.InputFormat"/>.</param>
    /// <param name="parts">Part markings the whole must cover. The whole is judged as covering them: a part it does not cover is an error, and so is a part that does not read as a marking. Each entry is a marking in one of the scheme's part formats, or any format the scheme reads. Empty or absent, the whole stands alone.</param>
    /// <param name="contextJson">Optional processing context as a JSON object string, forwarded verbatim to the engine. <c>{"role":"part","partOf":{"rendered":"..."}}</c> judges the input as a portion of the given whole marking. Omitted, the input is judged as a whole marking.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying what validate answers.</returns>
    /// <exception cref="ArgumentException"><paramref name="contextJson"/> is empty, is not valid JSON, or is not a JSON object.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeValidateResult> ValidateAsync(
        string input,
        string? surface = null,
        string? outputFormat = null,
        string? inputFormat = null,
        IEnumerable<string>? parts = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        using var contextDoc = CtxPayload.OptionalJsonObjectArgument(contextJson, nameof(contextJson));
        var body = new Dictionary<string, object?> { ["input"] = input };
        CtxPayload.PutIfSet(body, "inputFormat", inputFormat);
        CtxPayload.PutReaderOptions(body, surface, outputFormat, parts, contextDoc);
        return await _client.CachedPostCtxAsync("scheme.validate", "scheme/validate", body, ContextResultParser.ParseContextSchemeValidateResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Judge a marking given as facts, with no text: the reader's call for a
    /// marking a document or an author states as concepts rather than writes.
    ///
    /// <para>Pass facts only as they were stated. The
    /// <see cref="ValidateResult.Assertions"/> of an earlier result are already
    /// settled, and validating those can call a marking valid whose text was
    /// not. The answer is the one the text overload gives; facts carry no text,
    /// so there is no input format to name.</para>
    /// </summary>
    /// <param name="assertions">The facts as stated.</param>
    /// <param name="surface">Optional validation surface, named with one of the scheme's scope ids. Omitted, the marking is evaluated as a whole.</param>
    /// <param name="outputFormat">Optional output the returned marking is written in: a format id, or for a scheme that declares surfaces, a <c>surface/view</c> address or a view id. Omitted, the scheme's default format, or for a scheme that declares surfaces, each surface the result writes in its default for the marking's role.</param>
    /// <param name="parts">Part markings the whole must cover, judged as on the text overload.</param>
    /// <param name="contextJson">Optional processing context as a JSON object string, forwarded verbatim to the engine. A marking given as facts has no role unless the context gives one, and one with no role is written as a whole.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying what validate answers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assertions"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="contextJson"/> is empty, is not valid JSON, or is not a JSON object.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeValidateResult> ValidateAsync(
        IEnumerable<Assertion> assertions,
        string? surface = null,
        string? outputFormat = null,
        IEnumerable<string>? parts = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        if (assertions is null) throw new ArgumentNullException(nameof(assertions));
        using var contextDoc = CtxPayload.OptionalJsonObjectArgument(contextJson, nameof(contextJson));
        // The marking travels in exactly one of input and assertions; the route
        // answers 400 to a body carrying both, so there is no empty input here.
        var body = new Dictionary<string, object?> { ["assertions"] = CtxPayload.Assertions(assertions) };
        CtxPayload.PutReaderOptions(body, surface, outputFormat, parts, contextDoc);
        return await _client.CachedPostCtxAsync("scheme.validate", "scheme/validate", body, ContextResultParser.ParseContextSchemeValidateResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Explain, step by step, how the server settles a marking.
    ///
    /// <para>Takes what <see cref="ProcessAsync"/> takes apart from
    /// <c>outputs</c> and <c>suggestFor</c>, which only add to a process result,
    /// and returns the trace of that same processing: what was read, repaired,
    /// inferred and found wrong, and what was written. Reach for it when someone
    /// asks why a marking came out the way it did. No other operation returns
    /// the trace.</para>
    /// </summary>
    /// <param name="input">What was typed. Empty when <paramref name="assertions"/> carries the marking alone.</param>
    /// <param name="outputFormat">Optional output the trace's rendered marking is written in, as on <see cref="ProcessAsync"/>.</param>
    /// <param name="surface">Optional validation surface, named with one of the scheme's scope ids. Omitted, the marking is evaluated as a whole.</param>
    /// <param name="inputFormat">Optional name for what the text is read as, as on <see cref="ProcessAsync"/>. Omitted, recognition works it out.</param>
    /// <param name="assertions">Optional facts already chosen, settled together with <paramref name="input"/> as one marking.</param>
    /// <param name="contextJson">Optional processing context as a JSON object string, forwarded verbatim to the engine.</param>
    /// <param name="parts">Part markings the whole must cover, judged as on <see cref="ProcessAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the explanation trace.</returns>
    /// <exception cref="ArgumentException"><paramref name="contextJson"/> is empty, is not valid JSON, or is not a JSON object.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeExplainResult> ExplainAsync(
        string input,
        string? outputFormat = null,
        string? surface = null,
        string? inputFormat = null,
        IEnumerable<Assertion>? assertions = null,
        string? contextJson = null,
        IEnumerable<string>? parts = null,
        CancellationToken cancellationToken = default)
    {
        using var contextDoc = CtxPayload.OptionalJsonObjectArgument(contextJson, nameof(contextJson));
        var body = CtxPayload.AuthorBody(input, outputFormat, surface, inputFormat, assertions, contextDoc, parts);
        return await _client.CachedPostCtxAsync("scheme.explain", "scheme/explain",
            body, ContextResultParser.ParseContextSchemeExplainResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Get the concepts a picker may offer for a partial marking given as text.
    /// Facts use the separate overload; text and facts are never combined.
    /// </summary>
    /// <param name="input">Partial marking string to complete, including an empty string.</param>
    /// <param name="outputFormat">Optional output format for suggestion previews.</param>
    /// <param name="filter">Optional substring filter applied to candidate concepts.</param>
    /// <param name="surface">Optional suggestion surface declared by the scheme.</param>
    /// <param name="inputFormat">Optional format or view the text is read as.</param>
    /// <param name="includeGated">Whether to include candidates with unmet prerequisites.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the suggestions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeSuggestResult> SuggestAsync(
        string input,
        string? outputFormat = null,
        string? filter = null,
        string? surface = null,
        string? inputFormat = null,
        bool? includeGated = null,
        CancellationToken cancellationToken = default)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var body = new Dictionary<string, object?> { ["input"] = input };
        CtxPayload.PutIfSet(body, "inputFormat", inputFormat);
        PutSuggestOptions(body, outputFormat, filter, surface, includeGated);
        return await _client.CachedPostCtxAsync("scheme.suggest", "scheme/suggest", body,
            ContextResultParser.ParseContextSchemeSuggestResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Get the concepts a picker may offer beside the caller's stated facts.
    /// Facts carry no text, so this overload has no input format.
    /// </summary>
    /// <param name="assertions">The caller's facts, including an empty sequence.</param>
    /// <param name="outputFormat">Optional output format for suggestion previews.</param>
    /// <param name="filter">Optional substring filter applied to candidate concepts.</param>
    /// <param name="surface">Optional suggestion surface declared by the scheme.</param>
    /// <param name="includeGated">Whether to include candidates with unmet prerequisites.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the suggestions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assertions"/> is null.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeSuggestResult> SuggestAsync(
        IEnumerable<Assertion> assertions,
        string? outputFormat = null,
        string? filter = null,
        string? surface = null,
        bool? includeGated = null,
        CancellationToken cancellationToken = default)
    {
        if (assertions is null) throw new ArgumentNullException(nameof(assertions));
        var body = new Dictionary<string, object?> { ["assertions"] = CtxPayload.Assertions(assertions) };
        PutSuggestOptions(body, outputFormat, filter, surface, includeGated);
        return await _client.CachedPostCtxAsync("scheme.suggest", "scheme/suggest", body,
            ContextResultParser.ParseContextSchemeSuggestResult, cancellationToken).ConfigureAwait(false);
    }

    private static void PutSuggestOptions(Dictionary<string, object?> body,
        string? outputFormat, string? filter, string? surface, bool? includeGated)
    {
        CtxPayload.PutIfSet(body, "outputFormat", outputFormat);
        CtxPayload.PutIfSet(body, "filter", filter);
        CtxPayload.PutIfSet(body, "surface", surface);
        CtxPayload.PutIfSet(body, "includeGated", includeGated);
    }

    /// <summary>
    /// Get picker options for several categories in one request, keyed by
    /// category id.
    ///
    /// <para>Each category is answered as if its own assertions were not chosen
    /// yet, so a picker can offer the alternatives to its current selection,
    /// not only what can still be added beside it. A caller that processes the
    /// marking on the same edit gets the same map from
    /// <see cref="ProcessAsync"/> by naming the categories in its
    /// <c>suggestFor</c>, and saves the second request.</para>
    /// </summary>
    /// <param name="assertions">The caller's full set of chosen assertions.</param>
    /// <param name="categoryIds">Categories to answer for, one entry each in the result.</param>
    /// <param name="outputFormat">Optional format the suggestion previews are rendered in: a format id, or for a scheme that declares surfaces, an output address. There is no input format: this takes chosen facts, never text.</param>
    /// <param name="surface">Optional suggestion surface, named with one of the scheme's scope ids: restricts suggestions to the concepts and categories that surface owns.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying one <see cref="SuggestResult"/> per requested category id.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assertions"/> or <paramref name="categoryIds"/> is null.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached or did not answer within the request timeout, or its answer left out a requested category.</exception>
    public async Task<ContextSchemeSuggestForCategoriesResult> SuggestForCategoriesAsync(
        IEnumerable<Assertion> assertions,
        IEnumerable<string> categoryIds,
        string? outputFormat = null,
        string? surface = null,
        CancellationToken cancellationToken = default)
    {
        if (assertions is null) throw new ArgumentNullException(nameof(assertions));
        if (categoryIds is null) throw new ArgumentNullException(nameof(categoryIds));
        var requested = new List<string>(categoryIds);
        var body = new Dictionary<string, object?>
        {
            ["assertions"] = CtxPayload.Assertions(assertions),
            ["categoryIds"] = requested,
        };
        CtxPayload.PutIfSet(body, "outputFormat", outputFormat);
        CtxPayload.PutIfSet(body, "surface", surface);
        // The coverage check runs inside the cached call: an answer missing a
        // category must not be filed for every later caller with the same request.
        return await _client.CachedPostCtxAsync("scheme.suggestForCategories", "scheme/suggest-for-categories", body,
            json => AnswersEveryCategory(ContextResultParser.ParseContextSchemeSuggestForCategoriesResult(json), requested),
            cancellationToken).ConfigureAwait(false);
    }

    // A caller indexes the map by the ids it asked for, so a missing entry would
    // surface later as a KeyNotFoundException far from the request that caused it.
    private static ContextSchemeSuggestForCategoriesResult AnswersEveryCategory(
        ContextSchemeSuggestForCategoriesResult answer, IReadOnlyList<string> requested)
    {
        var missing = new List<string>();
        foreach (var id in requested)
            if (!answer.Result.ContainsKey(id) && !missing.Contains(id)) missing.Add(id);
        if (missing.Count > 0)
            throw new EzClassifyClientException(
                "The suggest-for-categories response answered no result for: " + string.Join(", ", missing));
        return answer;
    }

    /// <summary>
    /// Aggregate multiple markings into a single combined marking.
    ///
    /// <para>The result carries notices on the same terms as a process result:
    /// the notices the scheme attaches to the concepts in the rollup are already
    /// on the result.</para>
    /// </summary>
    /// <param name="inputs">Rendered markings to combine.</param>
    /// <param name="outputFormat">Optional output the combined marking is written in: a format id, or for a scheme that declares surfaces, an output address. Defaults to the scheme's default format; it selects output only and never narrows what may be read.</param>
    /// <param name="surface">Optional validation surface of the rollup, named with one of the scheme's scope ids. It names what the combined marking is, not what any one input was. Omitted, the rollup is evaluated as a whole marking.</param>
    /// <param name="inputFormat">Optional id of the format the input markings are written in, or for a scheme that declares surfaces, the view they are read as or one of that view's forms. Omitted, recognition works it out.</param>
    /// <param name="contextJson">Optional processing context as a JSON object string, forwarded verbatim to the engine. <c>{"role":"part","partOf":{"rendered":"..."}}</c> marks the rollup as a portion of the given whole marking. Omitted, the rollup is treated as a whole marking.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the combined marking.</returns>
    /// <exception cref="ArgumentException"><paramref name="contextJson"/> is empty, is not valid JSON, or is not a JSON object.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeAggregateResult> AggregateAsync(
        IEnumerable<string> inputs,
        string? outputFormat = null,
        string? surface = null,
        string? inputFormat = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        using var contextDoc = CtxPayload.OptionalJsonObjectArgument(contextJson, nameof(contextJson));
        var body = new Dictionary<string, object?> { ["inputs"] = new List<string>(inputs) };
        CtxPayload.PutIfSet(body, "inputFormat", inputFormat);
        CtxPayload.PutIfSet(body, "outputFormat", outputFormat);
        CtxPayload.PutIfSet(body, "surface", surface);
        CtxPayload.PutIfSet(body, "context", contextDoc);
        return await _client.CachedPostCtxAsync("scheme.aggregate", "scheme/aggregate", body, ContextResultParser.ParseContextSchemeAggregateResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Aggregate pre-parsed assertion sets into a single combined marking.
    ///
    /// <para>The lossless counterpart to <see cref="AggregateAsync(IEnumerable{string}, string?, string?, string?, string?, CancellationToken)"/>:
    /// each marking is transported as an already-parsed assertion array (<c>assertionSets</c>)
    /// rather than a rendered string, so scope-only assertions and capture values survive the
    /// round trip. The server skips the string-parse stage and runs the rest of the aggregate
    /// pipeline identically. Mutually exclusive with <c>inputs</c>.</para>
    /// </summary>
    /// <param name="assertionSets">One assertion array per marking to aggregate.</param>
    /// <param name="outputFormat">Optional output the combined marking is written in: a format id, or for a
    /// scheme that declares surfaces, an output address. Defaults to the scheme's default format.</param>
    /// <param name="surface">Optional validation surface of the rollup, named with one of the
    /// scheme's scope ids. Omitted, the marking is evaluated as a whole and every required
    /// category it owns must be covered; given a scope id, only the required
    /// categories that surface owns are demanded.</param>
    /// <param name="contextJson">Optional processing context as a JSON object string, forwarded verbatim to the engine. <c>{"role":"part","partOf":{"rendered":"..."}}</c> marks the rollup as a portion of the given whole marking. Omitted, the rollup is treated as a whole marking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The combined <see cref="AggregateResult"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="contextJson"/> is empty, is not valid JSON, or is not a JSON object.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeAggregateResult> AggregateAssertionsAsync(
        IEnumerable<IEnumerable<Assertion>> assertionSets,
        string? outputFormat = null,
        string? surface = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        using var contextDoc = CtxPayload.OptionalJsonObjectArgument(contextJson, nameof(contextJson));
        var sets = new List<object>();
        foreach (var set in assertionSets) sets.Add(CtxPayload.Assertions(set));
        var body = new Dictionary<string, object?> { ["assertionSets"] = sets };
        CtxPayload.PutIfSet(body, "outputFormat", outputFormat);
        CtxPayload.PutIfSet(body, "surface", surface);
        CtxPayload.PutIfSet(body, "context", contextDoc);
        return await _client.CachedPostCtxAsync("scheme.aggregateAssertions", "scheme/aggregate", body, ContextResultParser.ParseContextSchemeAggregateResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Scan free text for markings.
    ///
    /// <para>Each match's <see cref="ScanMatch.Result"/> is what validate
    /// answers for that marking, read in the role it was found in. A scan reads
    /// a document and does not author it, so there are no fix paths; a caller
    /// that wants to correct one region processes that region.</para>
    /// </summary>
    /// <param name="text">Free text to scan for markings.</param>
    /// <param name="outputFormat">Optional output each match is written in: a format id, or for a scheme that declares surfaces, an output address. Omitted, each match is written as its <see cref="ScanMatch.Type"/> describes. It selects output only and never narrows what may be read.</param>
    /// <param name="inputFormat">Optional id of the format the markings in the text are written in, or for a scheme that declares surfaces, the view they are read as or one of that view's forms. Omitted, recognition works it out.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying every match found, in document order.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeScanResult> ScanAsync(
        string text,
        string? outputFormat = null,
        string? inputFormat = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["text"] = text };
        CtxPayload.PutIfSet(body, "inputFormat", inputFormat);
        CtxPayload.PutIfSet(body, "outputFormat", outputFormat);
        return await _client.CachedPostCtxAsync("scheme.scan", "scheme/scan", body, ContextResultParser.ParseContextSchemeScanResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Render assertions directly into a marking string without re-parsing.</summary>
    /// <param name="assertions">Assertions to render.</param>
    /// <param name="outputFormat">Optional output the marking is written in: a format id, or for a scheme that declares surfaces, an output address. Omitted, the scheme's default format.</param>
    /// <param name="preservation">Whether the server may project facts or must preserve every fact.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the rendered marking.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeRenderResult> RenderAsync(
        IEnumerable<Assertion> assertions,
        string? outputFormat = null,
        RenderPreservation? preservation = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["assertions"] = CtxPayload.Assertions(assertions) };
        CtxPayload.PutIfSet(body, "outputFormat", outputFormat);
        if (preservation is not null)
        {
            body["preservation"] = preservation.Value.ToWire();
        }
        return await _client.CachedPostCtxAsync("scheme.render", "scheme/render", body, ContextResultParser.ParseContextSchemeRenderResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Return the canonical normalized form of a marking string.</summary>
    /// <param name="input">Marking string to normalize.</param>
    /// <param name="outputFormat">Optional output the canonical form is written in: a format id, or for a scheme that declares surfaces, an output address. Defaults to the scheme's default format; it selects output only and never narrows what may be read.</param>
    /// <param name="inputFormat">Optional id of the format the input is written in, or for a scheme that declares surfaces, the view it is read as or one of that view's forms. Omitted, recognition works it out.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the canonical form.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeNormalizeResult> NormalizeAsync(
        string input,
        string? outputFormat = null,
        string? inputFormat = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["input"] = input };
        CtxPayload.PutIfSet(body, "inputFormat", inputFormat);
        CtxPayload.PutIfSet(body, "outputFormat", outputFormat);
        return await _client.CachedPostCtxAsync("scheme.normalize", "scheme/normalize", body, ContextResultParser.ParseContextSchemeNormalizeResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Check whether a set of child markings is covered by a parent marking.</summary>
    /// <param name="children">Part markings to test.</param>
    /// <param name="parent">Whole marking the parts are tested against.</param>
    /// <param name="outputFormat">Optional output markings in the report are written in: a format id, or for a scheme that declares surfaces, an output address. Defaults to the scheme's default format; it selects output only and never narrows what may be read.</param>
    /// <param name="inputFormat">Optional id of the format the parent and part markings are written in, or for a scheme that declares surfaces, the view they are read as or one of that view's forms. Omitted, recognition works it out.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the coverage report.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeCoverageResult> CheckCoverageAsync(
        IEnumerable<string> children,
        string parent,
        string? outputFormat = null,
        string? inputFormat = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["children"] = new List<string>(children),
            ["parent"] = parent,
        };
        CtxPayload.PutIfSet(body, "inputFormat", inputFormat);
        CtxPayload.PutIfSet(body, "outputFormat", outputFormat);
        var json = await _client.PostCtxAsync("scheme/check-coverage", body, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextSchemeCoverageResult(json);
    }

    /// <summary>Describe a concept's usage: definition, guidance, and applicable rules.</summary>
    /// <param name="conceptId">Concept to describe.</param>
    /// <param name="categoryId">Category the concept belongs to.</param>
    /// <param name="view">Optional view to name the concept in: a key of the concept view's <see cref="ConceptView.Views"/>. Omitted, its default view.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the concept description.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeConceptDescriptionResult> DescribeConceptAsync(
        string conceptId,
        string categoryId,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"?conceptId={Uri.EscapeDataString(conceptId)}&categoryId={Uri.EscapeDataString(categoryId)}";
        if (!string.IsNullOrEmpty(view)) query += $"&view={Uri.EscapeDataString(view!)}";
        return await _client.CachedGetCtxAsync("scheme.describeConcept", "scheme/describe-concept" + query, ContextResultParser.ParseContextSchemeConceptDescriptionResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Diff two marking strings, returning added, removed, and common assertions.</summary>
    /// <param name="a">First marking.</param>
    /// <param name="b">Second marking.</param>
    /// <param name="inputFormat">Optional id of the format both markings are written in, or for a scheme that declares surfaces, the view they are read as or one of that view's forms. Omitted, recognition works it out. There is no output format: the result is assertions, not a rendered string.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying added, removed, and common assertions.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeDiffResult> DiffAsync(
        string a,
        string b,
        string? inputFormat = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["a"] = a, ["b"] = b };
        CtxPayload.PutIfSet(body, "inputFormat", inputFormat);
        return await _client.CachedPostCtxAsync("scheme.diff", "scheme/diff", body, ContextResultParser.ParseContextSchemeDiffResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Substring-filter the context's concepts. Returns matched ids and per-category counts.</summary>
    /// <param name="query">Substring to match against concept names and ids.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying matched ids and per-category counts.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeFilterConceptsResult> FilterConceptsAsync(string query, CancellationToken cancellationToken = default)
    {
        var json = await _client.PostCtxAsync("scheme/filter-concepts", new { query }, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextSchemeFilterConceptsResult(json);
    }

    /// <summary>
    /// Collect concept-attached notices for the given assertions.
    ///
    /// <para>Use this when you hold assertions on their own, for example a set a
    /// user is still assembling. If you already have a process or aggregate
    /// result, read <c>Notices</c> off that result instead: it carries the same
    /// list, collected the same way, so reading notices for a processed marking
    /// does not take a second call.</para>
    /// </summary>
    /// <param name="assertions">Assertions whose concepts may carry notices.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying every notice the assertions raise.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeNoticesResult> NoticesAsync(
        IEnumerable<Assertion> assertions,
        CancellationToken cancellationToken = default)
    {
        var json = await _client.PostCtxAsync(
            "scheme/notices",
            new { assertions = CtxPayload.Assertions(assertions) },
            cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextSchemeNoticesResult(json);
    }

    /// <summary>Build a render-ready structural view of the context's composed scheme.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the structural view.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeViewResult> ViewAsync(CancellationToken cancellationToken = default)
    {
        var json = await _client.PostCtxAsync("scheme/view", new { }, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextSchemeViewResult(json);
    }

    // --- scheme sub-resources ------------------------------------------------
    //
    // Reads of the composed scheme rather than operations over it. They answer
    // flat, outside the Context envelope every operation above returns: there is
    // no engine run to resolve, so there is no resolution block to report. The
    // resolved version still rides the response header and lands on
    // EzClassifyClient.ResolvedVersion like any other context call.
    //
    // Each but the label presets takes an optional scheme key. It selects which
    // of the context's schemes to read, defaulting to the primary one; naming a
    // crosswalk's source or target scheme is what it exists for. The label
    // presets are the primary scheme's, the one apply-preset expands them in, and
    // the server refuses a scheme key there. There is no layers parameter on
    // this surface: the layers belong to the context.

    /// <summary>Get the summary metadata for the context's composed scheme.</summary>
    /// <param name="scheme">
    /// Context-local key, or unambiguous artifact id, of the scheme to read.
    /// Defaults to the context's primary scheme when omitted.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<SchemeSummary> SummaryAsync(string? scheme = null, CancellationToken cancellationToken = default)
    {
        var json = await _client.GetCtxAsync("scheme" + CtxPayload.SchemeQuery(scheme), cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseSchemeSummary(json);
    }

    /// <summary>List the concepts of the context's composed scheme as concept views.</summary>
    /// <param name="scheme">Context-local key or artifact id of the scheme to read; defaults to the primary scheme.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<IReadOnlyList<ConceptView>> ConceptsAsync(string? scheme = null, CancellationToken cancellationToken = default)
    {
        var json = await _client.GetCtxAsync("scheme/concepts" + CtxPayload.SchemeQuery(scheme), cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseSchemeConcepts(json);
    }

    /// <summary>List the categories of the context's composed scheme as category views, in declared order.</summary>
    /// <param name="scheme">Context-local key or artifact id of the scheme to read; defaults to the primary scheme.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<IReadOnlyList<CategoryView>> CategoriesAsync(string? scheme = null, CancellationToken cancellationToken = default)
    {
        var json = await _client.GetCtxAsync("scheme/categories" + CtxPayload.SchemeQuery(scheme), cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseSchemeCategories(json);
    }

    /// <summary>List the formats the context's composed scheme writes, as format views.</summary>
    /// <param name="scheme">Context-local key or artifact id of the scheme to read; defaults to the primary scheme.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<IReadOnlyList<FormatView>> FormatsAsync(string? scheme = null, CancellationToken cancellationToken = default)
    {
        var json = await _client.GetCtxAsync("scheme/formats" + CtxPayload.SchemeQuery(scheme), cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseSchemeFormats(json);
    }

    /// <summary>Get the parse grammar of the context's composed scheme.</summary>
    /// <param name="scheme">Context-local key or artifact id of the scheme to read; defaults to the primary scheme.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<SchemeGrammar> GrammarAsync(string? scheme = null, CancellationToken cancellationToken = default)
    {
        var json = await _client.GetCtxAsync("scheme/grammar" + CtxPayload.SchemeQuery(scheme), cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseSchemeGrammar(json);
    }

    /// <summary>
    /// Get the scopes the context's composed scheme declares, in declaration
    /// order: the order a selector offers them in. Empty when it declares none.
    /// </summary>
    /// <param name="scheme">Context-local key or artifact id of the scheme to read; defaults to the primary scheme.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<IReadOnlyList<SchemeScope>> ScopesAsync(string? scheme = null, CancellationToken cancellationToken = default)
    {
        var json = await _client.GetCtxAsync("scheme/scopes" + CtxPayload.SchemeQuery(scheme), cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseSchemeScopes(json);
    }

    /// <summary>
    /// List the label presets of the context's primary scheme, the scheme
    /// <see cref="ApplyPresetAsync(string, string?, CancellationToken)"/> expands
    /// them in. Each entry carries name and scope only; expand one with that
    /// call. Empty when the scheme declares none.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<IReadOnlyList<LabelPresetView>> LabelPresetsAsync(CancellationToken cancellationToken = default)
    {
        var json = await _client.GetCtxAsync("scheme/label-presets", cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseSchemeLabelPresets(json);
    }

    /// <summary>
    /// Expand a label preset against the context's composed scheme.
    ///
    /// <para>Unlike the sibling sub-resources this one answers in the standard
    /// Context operation envelope: expanding a preset runs its marking through
    /// the engine, so the reply carries a resolution block and what validate
    /// answers for the marking the preset stands for. A preset is a stored
    /// marking, so there is nothing to correct and no fix path to offer.</para>
    /// </summary>
    /// <param name="presetId">Id of the preset to expand.</param>
    /// <param name="outputFormat">Optional output the expanded marking is written in: a format id, or for a scheme that declares surfaces, an output address. Omitted, the scheme's default format.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ArgumentException"><paramref name="presetId"/> is null or empty.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextSchemeValidateResult> ApplyPresetAsync(
        string presetId,
        string? outputFormat = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(presetId))
            throw new ArgumentException("A preset id must not be empty.", nameof(presetId));

        var body = new Dictionary<string, object?> { ["presetId"] = presetId };
        CtxPayload.PutIfSet(body, "outputFormat", outputFormat);
        return await _client.CachedPostCtxAsync("scheme.applyPreset", "scheme/apply-preset", body, ContextResultParser.ParseContextSchemeValidateResult, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Guide-domain operations bound to a connected Context. Reached via
/// <see cref="EzClassifyClient.Guide"/>; routes through
/// <c>/v1/ctx/{ctxId}/{version}/guide/*</c>.
/// </summary>
public sealed class ContextGuideOps
{
    private readonly EzClassifyClient _client;

    internal ContextGuideOps(EzClassifyClient client) => _client = client;

    /// <summary>Scan free text for guide rule matches. Pass <paramref name="guideId"/> to narrow to one guide.</summary>
    /// <param name="text">Free text to scan for guide rule matches.</param>
    /// <param name="guideId">Optional guide id; omitted, every guide in the Context is considered.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying every rule match found.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextGuideScanResult> ScanAsync(
        string text,
        string? guideId = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["text"] = text };
        if (guideId is not null) body["guideId"] = guideId;
        var json = await _client.PostCtxAsync("guide/scan", body, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextGuideScanResult(json);
    }

    /// <summary>Evaluate a guide with the given rule selection.</summary>
    /// <param name="guideId">Guide to evaluate.</param>
    /// <param name="selectedRuleIds">Rules the analyst selected.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the evaluation result.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextGuideEvaluationResult> EvaluateAsync(
        string guideId,
        IEnumerable<string> selectedRuleIds,
        CancellationToken cancellationToken = default)
    {
        var json = await _client.PostCtxAsync(
            "guide/evaluate",
            new { guideId, selectedRuleIds = new List<string>(selectedRuleIds) },
            cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextGuideEvaluationResult(json);
    }

    /// <summary>Evaluate selected rules across every guide in the Context.</summary>
    /// <param name="selectedRuleIds">Rules the analyst selected across the guide set.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the combined evaluation result.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextGuideEvaluationResult> EvaluateManyAsync(
        IEnumerable<string> selectedRuleIds,
        CancellationToken cancellationToken = default)
    {
        var json = await _client.PostCtxAsync(
            "guide/evaluate",
            new { allGuides = true, selectedRuleIds = new List<string>(selectedRuleIds) },
            cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextGuideEvaluationResult(json);
    }

    /// <summary>Substring-filter guide rules. Pass <paramref name="guideId"/> to restrict to one guide.</summary>
    /// <param name="query">Substring to match against rule names and ids.</param>
    /// <param name="guideId">Optional guide id; omitted, every guide in the Context is considered.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying matched rule ids and counts.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextGuideFilterRulesResult> FilterRulesAsync(
        string query,
        string? guideId = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["query"] = query };
        if (guideId is not null) body["guideId"] = guideId;
        var json = await _client.PostCtxAsync("guide/filter-rules", body, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextGuideFilterRulesResult(json);
    }

    /// <summary>Describe a single guide rule (name, outcome, dependencies).</summary>
    /// <param name="ruleId">Rule to describe.</param>
    /// <param name="guideId">Optional guide id; omitted, every guide in the Context is considered.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the rule description.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextGuideRuleDescriptionResult> DescribeRuleAsync(
        string ruleId,
        string? guideId = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["ruleId"] = ruleId };
        if (guideId is not null) body["guideId"] = guideId;
        var json = await _client.PostCtxAsync("guide/describe-rule", body, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextGuideRuleDescriptionResult(json);
    }

    /// <summary>Build a render-ready structural view of a guide (defaults to the first when <paramref name="guideId"/> is omitted).</summary>
    /// <param name="guideId">Optional guide id; omitted, the first guide in the Context is used.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the structural view.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextGuideViewResult> ViewAsync(string? guideId = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>();
        if (guideId is not null) body["guideId"] = guideId;
        var json = await _client.PostCtxAsync("guide/view", body, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextGuideViewResult(json);
    }
}

/// <summary>
/// Access-control operations bound to a connected Context. Reached via
/// <see cref="EzClassifyClient.Access"/>; routes through
/// <c>/v1/ctx/{ctxId}/{version}/access/*</c>.
/// </summary>
public sealed class ContextAccessOps
{
    private readonly EzClassifyClient _client;

    internal ContextAccessOps(EzClassifyClient client) => _client = client;

    /// <summary>Describe every access rule in the Context's access policy.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Every access rule in the Context's access policy, in declaration order.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<IReadOnlyList<AccessRuleDescription>> RulesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _client.CachedGetCtxAsync("access.describeAllAccessRules", "access/rules", ContextResultParser.ParseContextAccessRules, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>List access-rule ids in declaration order.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Access-rule ids in declaration order.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<IReadOnlyList<string>> RuleIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var rules = await RulesAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<string>(rules.Count);
        foreach (var rule in rules) ids.Add(rule.Rule.RuleId);
        return ids;
    }

    /// <summary>
    /// Check whether a subject's user attributes satisfy the context's access
    /// policy for the given data assertions.
    /// </summary>
    /// <param name="dataAssertions">Assertions describing the tagged data.</param>
    /// <param name="userAttributesJson">Subject attributes as a JSON object string (e.g. <c>{"clearance":"HIGH"}</c>).</param>
    /// <param name="contextJson">Optional comparison data the marking does not carry, such as distribution organizations, as a JSON object string. When decision audit is enabled, the deployment names the user and resource ids it requires in here.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ArgumentException"><paramref name="userAttributesJson"/> or <paramref name="contextJson"/> is empty, is not valid JSON, or is not a JSON object.</exception>
    public async Task<ContextAccessDecisionResult> CheckAsync(
        IEnumerable<Assertion> dataAssertions,
        string userAttributesJson,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        using var userAttributesDoc = CtxPayload.JsonObjectArgument(userAttributesJson, nameof(userAttributesJson));
        using var contextDoc = CtxPayload.OptionalJsonObjectArgument(contextJson, nameof(contextJson));
        var body = new Dictionary<string, object?>
        {
            ["assertions"] = CtxPayload.Assertions(dataAssertions),
            ["userAttributes"] = userAttributesDoc.RootElement,
        };
        CtxPayload.PutIfSet(body, "context", contextDoc);
        return await _client.DecisionPostCtxAsync("access.check", "access/check", body, ContextResultParser.ParseContextAccessDecisionResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Check access from a marking string: the server processes the marking
    /// against the context's scheme, then evaluates the access policy.
    /// </summary>
    /// <param name="marking">The marking string to evaluate access for.</param>
    /// <param name="userAttributesJson">Subject attributes as a JSON object string.</param>
    /// <param name="contextJson">Optional comparison data the marking does not carry, such as distribution organizations, as a JSON object string. When decision audit is enabled, the deployment names the user and resource ids it requires in here.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ArgumentException"><paramref name="userAttributesJson"/> or <paramref name="contextJson"/> is empty, is not valid JSON, or is not a JSON object.</exception>
    public async Task<ContextAccessDecisionResult> CheckByMarkingAsync(
        string marking,
        string userAttributesJson,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        using var userAttributesDoc = CtxPayload.JsonObjectArgument(userAttributesJson, nameof(userAttributesJson));
        using var contextDoc = CtxPayload.OptionalJsonObjectArgument(contextJson, nameof(contextJson));
        var body = new Dictionary<string, object?>
        {
            ["marking"] = marking,
            ["userAttributes"] = userAttributesDoc.RootElement,
        };
        CtxPayload.PutIfSet(body, "context", contextDoc);
        return await _client.DecisionPostCtxAsync("context.checkAccess", "access/check", body, ContextResultParser.ParseContextAccessDecisionResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Describe the aggregated access requirements for a marking.</summary>
    /// <param name="marking">Rendered marking whose requirements are described.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the aggregated requirements.</returns>
    /// <exception cref="ArgumentException"><paramref name="marking"/> is empty.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextAccessRequirementsResult> DescribeRequirementsAsync(
        string marking,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(marking))
            throw new ArgumentException("A marking must not be empty.", nameof(marking));
        var json = await _client.PostCtxAsync("access/describe-requirements", new { marking }, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextAccessRequirementsResult(json);
    }

    /// <summary>Describe access requirements for structured target assertions.</summary>
    /// <param name="assertions">Target assertions whose requirements are described.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the aggregated requirements.</returns>
    /// <exception cref="ArgumentException"><paramref name="assertions"/> is empty.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public Task<ContextAccessRequirementsResult> DescribeRequirementsAsync(
        IEnumerable<Assertion> assertions,
        CancellationToken cancellationToken = default) =>
        DescribeRequirementsAsync(marking: null, assertions, cancellationToken);

    /// <summary>Describe access requirements for a marking merged with structured target assertions.</summary>
    /// <param name="marking">Rendered marking, or null to describe the assertions alone.</param>
    /// <param name="assertions">Target assertions merged with the marking.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the aggregated requirements.</returns>
    /// <exception cref="ArgumentException">Neither a marking nor any assertion was supplied.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextAccessRequirementsResult> DescribeRequirementsAsync(
        string? marking,
        IEnumerable<Assertion> assertions,
        CancellationToken cancellationToken = default)
    {
        var values = CtxPayload.Assertions(assertions);
        if (string.IsNullOrEmpty(marking) && values.Count == 0)
            throw new ArgumentException("A marking, one or more assertions, or both are required.", nameof(assertions));

        var body = new Dictionary<string, object?> { ["assertions"] = values };
        // An empty marking is treated as absent by the guard above, so it must
        // not go on the wire as an empty-string marking either.
        if (!string.IsNullOrEmpty(marking)) body["marking"] = marking;
        var json = await _client.PostCtxAsync("access/describe-requirements", body, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextAccessRequirementsResult(json);
    }

    /// <summary>Describe a single access rule by id.</summary>
    /// <param name="ruleId">Access rule to describe.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the rule description.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextAccessRuleDescriptionResult> DescribeRuleAsync(
        string ruleId,
        CancellationToken cancellationToken = default)
    {
        var json = await _client.PostCtxAsync("access/describe-rule", new { ruleId }, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextAccessRuleDescriptionResult(json);
    }
}

/// <summary>
/// Crosswalk operations bound to a connected Context. Reached via
/// <see cref="EzClassifyClient.Crosswalk"/>; routes through
/// <c>/v1/ctx/{ctxId}/{version}/crosswalk/*</c>.
/// </summary>
public sealed class ContextCrosswalkOps
{
    private readonly EzClassifyClient _client;

    internal ContextCrosswalkOps(EzClassifyClient client) => _client = client;

    /// <summary>Translate a rendered source marking through a declared directional crosswalk.</summary>
    /// <param name="crosswalkId">Declared directional crosswalk to translate through.</param>
    /// <param name="marking">Rendered source marking.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the translated marking.</returns>
    /// <exception cref="ArgumentException"><paramref name="crosswalkId"/> or <paramref name="marking"/> is empty.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextCrosswalkTranslationResult> TranslateAsync(
        string crosswalkId,
        string marking,
        CancellationToken cancellationToken = default)
    {
        ValidateCrosswalkId(crosswalkId);
        if (string.IsNullOrEmpty(marking))
            throw new ArgumentException("A source marking must not be empty.", nameof(marking));
        var json = await _client.PostCtxAsync(
            "crosswalk/translate",
            new { crosswalkId, marking },
            cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextCrosswalkTranslationResult(json);
    }

    /// <summary>Translate structured source assertions through a declared directional crosswalk.</summary>
    /// <param name="crosswalkId">Declared directional crosswalk to translate through.</param>
    /// <param name="assertions">Structured source assertions.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the translated marking.</returns>
    /// <exception cref="ArgumentException"><paramref name="crosswalkId"/> is empty, or no source assertion was supplied.</exception>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextCrosswalkTranslationResult> TranslateAsync(
        string crosswalkId,
        IEnumerable<Assertion> assertions,
        CancellationToken cancellationToken = default)
    {
        ValidateCrosswalkId(crosswalkId);
        var values = CtxPayload.Assertions(assertions);
        if (values.Count == 0)
            throw new ArgumentException("At least one source assertion is required.", nameof(assertions));
        var json = await _client.PostCtxAsync(
            "crosswalk/translate",
            new { crosswalkId, assertions = values },
            cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextCrosswalkTranslationResult(json);
    }

    private static void ValidateCrosswalkId(string crosswalkId)
    {
        if (string.IsNullOrEmpty(crosswalkId))
            throw new ArgumentException("A crosswalk id must not be empty.", nameof(crosswalkId));
    }
}

/// <summary>
/// Document-level operations bound to a connected Context. Reached via
/// <see cref="EzClassifyClient.Document"/>; routes through
/// <c>/v1/ctx/{ctxId}/{version}/document/*</c>.
/// </summary>
public sealed class ContextDocumentOps
{
    private readonly EzClassifyClient _client;

    internal ContextDocumentOps(EzClassifyClient client) => _client = client;

    /// <summary>
    /// Run a full document audit: whole-marking adequacy, per-part coverage,
    /// and unmarked-content detection over <paramref name="text"/>.
    /// </summary>
    /// <param name="text">Document text to audit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the audit report.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public Task<ContextDocumentAuditResult> AuditAsync(string text, CancellationToken cancellationToken = default) =>
        AuditAsync(text, guideDeterminations: null, responseProfile: null, cancellationToken);

    /// <summary>Run a document audit with optional analyst determinations and response projection.</summary>
    /// <param name="text">Document text to audit.</param>
    /// <param name="guideDeterminations">Analyst determinations to fold into the audit, or null.</param>
    /// <param name="responseProfile">Optional projection controlling how much of the report the server returns.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Context envelope carrying the audit report.</returns>
    /// <exception cref="EzClassifyClientException">The server rejected the request, could not be reached, or did not answer within the request timeout.</exception>
    public async Task<ContextDocumentAuditResult> AuditAsync(
        string text,
        GuideDeterminationInput? guideDeterminations,
        DocumentResponseProfile? responseProfile = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["text"] = text };
        if (guideDeterminations is not null) body["guideDeterminations"] = CtxPayload.GuideDeterminations(guideDeterminations);
        if (responseProfile is not null) body["responseProfile"] = responseProfile.Value.ToWire();
        var json = await _client.PostCtxAsync("document/audit", body, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextDocumentAuditResult(json);
    }

    /// <summary>
    /// Audit a document once and assess authored, authoritative, and advisory
    /// projected access scenarios for the supplied immutable subject attributes.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="userAttributesJson"/> or <paramref name="contextJson"/> is empty, is not valid JSON, or is not a JSON object.</exception>
    public async Task<ContextDocumentAccessAssessmentResult> AssessAccessAsync(
        string text,
        string userAttributesJson,
        string? contextJson = null,
        GuideDeterminationInput? guideDeterminations = null,
        DocumentResponseProfile? responseProfile = null,
        CancellationToken cancellationToken = default)
    {
        using var userAttributesDoc = CtxPayload.JsonObjectArgument(userAttributesJson, nameof(userAttributesJson));
        using var contextDoc = CtxPayload.OptionalJsonObjectArgument(contextJson, nameof(contextJson));
        var body = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["userAttributes"] = userAttributesDoc.RootElement,
        };
        CtxPayload.PutIfSet(body, "context", contextDoc);
        if (guideDeterminations is not null) body["guideDeterminations"] = CtxPayload.GuideDeterminations(guideDeterminations);
        if (responseProfile is not null) body["responseProfile"] = responseProfile.Value.ToWire();
        var json = await _client.PostCtxAsync("document/assess-access", body, cancellationToken).ConfigureAwait(false);
        return ContextResultParser.ParseContextDocumentAccessAssessmentResult(json);
    }
}

/// <summary>Helpers for building payload-only context request bodies.</summary>
internal static class CtxPayload
{
    /// <summary>
    /// Parse a caller-supplied JSON object argument. Anything the caller can get
    /// wrong here (empty text, invalid JSON, a value that is not an object)
    /// becomes an <see cref="ArgumentException"/> naming the parameter rather
    /// than a parser exception or a mis-shaped request body.
    /// </summary>
    internal static JsonDocument JsonObjectArgument(string json, string paramName)
    {
        if (json is null) throw new ArgumentNullException(paramName);
        if (json.Trim().Length == 0)
            throw new ArgumentException("A JSON object is required.", paramName);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Value is not valid JSON.", paramName, ex);
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            throw new ArgumentException("Value must be a JSON object.", paramName);
        }
        return doc;
    }

    /// <summary>
    /// <see cref="JsonObjectArgument"/> for an optional argument: null means the
    /// caller left it out and yields null, while anything else must still be a
    /// JSON object.
    /// </summary>
    internal static JsonDocument? OptionalJsonObjectArgument(string? json, string paramName) =>
        json is null ? null : JsonObjectArgument(json, paramName);

    /// <summary>
    /// Build the optional <c>?scheme=</c> query for a scheme sub-resource, or an
    /// empty string when the caller wants the context's primary scheme. A
    /// whitespace-only key is treated as absent rather than sent as a blank
    /// selector the server would answer 404 for.
    /// </summary>
    public static string SchemeQuery(string? scheme) =>
        string.IsNullOrWhiteSpace(scheme) ? "" : $"?scheme={Uri.EscapeDataString(scheme!)}";

    /// <summary>
    /// The body process and explain share: text and chosen facts settled as one
    /// marking, with the options that say how to read and write it. Explain
    /// takes exactly this; process adds the outputs and categories it answers.
    /// </summary>
    public static Dictionary<string, object?> AuthorBody(
        string input,
        string? outputFormat,
        string? surface,
        string? inputFormat,
        IEnumerable<Assertion>? assertions,
        JsonDocument? context,
        IEnumerable<string>? parts)
    {
        var body = new Dictionary<string, object?> { ["input"] = input };
        PutIfSet(body, "inputFormat", inputFormat);
        PutIfSet(body, "outputFormat", outputFormat);
        PutIfSet(body, "surface", surface);
        PutAssertionsIfSet(body, assertions);
        PutIfSet(body, "context", context);
        PutIfSet(body, "parts", parts);
        return body;
    }

    /// <summary>
    /// Set the options validate takes whether the marking arrives as text or as
    /// facts: what it is judged as, and how it is written back.
    /// </summary>
    public static void PutReaderOptions(
        Dictionary<string, object?> body,
        string? surface,
        string? outputFormat,
        IEnumerable<string>? parts,
        JsonDocument? context)
    {
        PutIfSet(body, "outputFormat", outputFormat);
        PutIfSet(body, "surface", surface);
        PutIfSet(body, "context", context);
        PutIfSet(body, "parts", parts);
    }

    /// <summary>Set an optional body field under its wire name when the caller supplied a value.</summary>
    public static void PutIfSet(Dictionary<string, object?> body, string wireName, string? value)
    {
        if (value is not null) body[wireName] = value;
    }

    /// <summary>
    /// Set an optional boolean body field. Null leaves the key off so the
    /// server applies its default; false is a real answer and is sent.
    /// </summary>
    public static void PutIfSet(Dictionary<string, object?> body, string wireName, bool? value)
    {
        if (value is not null) body[wireName] = value.Value;
    }

    /// <summary>Set an optional JSON-object body field from a parsed caller argument.</summary>
    public static void PutIfSet(Dictionary<string, object?> body, string wireName, JsonDocument? value)
    {
        if (value is not null) body[wireName] = value.RootElement;
    }

    /// <summary>Set an optional string-list body field when the caller supplied a value.</summary>
    public static void PutIfSet(Dictionary<string, object?> body, string wireName, IEnumerable<string>? value)
    {
        if (value is not null) body[wireName] = new List<string>(value);
    }

    /// <summary>Set the optional <c>assertions</c> field in its wire shape when the caller supplied any.</summary>
    public static void PutAssertionsIfSet(Dictionary<string, object?> body, IEnumerable<Assertion>? assertions)
    {
        if (assertions is not null) body["assertions"] = Assertions(assertions);
    }

    /// <summary>
    /// Project assertions to the camelCase wire shape the engine expects, carrying
    /// every field the contract's <c>Assertion</c> declares. Assertion arrays are the
    /// engine's state transport: dropping <c>expiresAt</c> would silently strip an
    /// expiry from an access check, and dropping provenance or the replaced concept
    /// would lose explanation input on the next call. The confidence token goes out
    /// exactly as it arrived, including a value this SDK does not know; the server
    /// that sent it decides whether it may come back.
    /// </summary>
    public static List<object> Assertions(IEnumerable<Assertion> assertions)
    {
        var list = new List<object>();
        foreach (var a in assertions)
        {
            var entry = new Dictionary<string, object?>
            {
                ["conceptId"] = a.ConceptId,
                ["categoryId"] = a.CategoryId,
                ["confidence"] = a.Confidence.ToWire(),
                ["source"] = a.Source,
            };
            if (a.SourceRule is not null) entry["sourceRule"] = a.SourceRule;
            if (a.ExpiresAt is not null) entry["expiresAt"] = a.ExpiresAt;
            if (a.Value is not null) entry["value"] = a.Value;
            if (a.Metadata is not null) entry["metadata"] = a.Metadata.Value;
            if (a.ReplacedConceptId is not null) entry["replacedConceptId"] = a.ReplacedConceptId;
            if (a.Provenance is not null) entry["provenance"] = SourceReference(a.Provenance);
            list.Add(entry);
        }
        return list;
    }

    private static Dictionary<string, object?> SourceReference(SourceReference source)
    {
        var attribution = new Dictionary<string, object?>
        {
            ["name"] = source.Attribution.Name,
            ["license"] = source.Attribution.License,
        };
        if (source.Attribution.LicenseUrl is not null) attribution["licenseUrl"] = source.Attribution.LicenseUrl;
        if (source.Attribution.Citation is not null) attribution["citation"] = source.Attribution.Citation;
        var value = new Dictionary<string, object?>
        {
            ["providerId"] = source.ProviderId,
            ["attribution"] = attribution,
            ["reproducibility"] = source.Reproducibility.ToWire(),
        };
        if (source.SnapshotId is not null) value["snapshotId"] = source.SnapshotId;
        if (source.Locator is not null) value["locator"] = source.Locator;
        return value;
    }

    /// <summary>Project analyst determinations to their exact camel-case wire shape.</summary>
    public static object GuideDeterminations(GuideDeterminationInput input)
    {
        var findings = new List<object>();
        foreach (var finding in input.Findings)
        {
            var item = new Dictionary<string, object?>
            {
                ["findingId"] = finding.FindingId,
                ["documentDigest"] = finding.DocumentDigest,
                ["decision"] = finding.Decision.ToWire(),
                ["reason"] = finding.Reason,
            };
            if (finding.ProposalOrigin is not null) item["proposalOrigin"] = finding.ProposalOrigin.Value.ToWire();
            findings.Add(item);
        }

        var manual = new List<object>();
        foreach (var selection in input.ManualSelections)
        {
            var item = new Dictionary<string, object?>
            {
                ["documentDigest"] = selection.DocumentDigest,
                ["rule"] = RuleReference(selection.Rule),
                ["evidence"] = Evidence(selection.Evidence),
                ["reason"] = selection.Reason,
            };
            if (selection.ProposalOrigin is not null) item["proposalOrigin"] = selection.ProposalOrigin.Value.ToWire();
            manual.Add(item);
        }
        return new Dictionary<string, object?> { ["findings"] = findings, ["manualSelections"] = manual };
    }

    private static List<object> Evidence(IEnumerable<ContentEvidenceSpan> evidence)
    {
        var values = new List<object>();
        foreach (var span in evidence)
        {
            var item = new Dictionary<string, object?>
            {
                ["start"] = span.Start,
                ["end"] = span.End,
                ["offsetEncoding"] = span.OffsetEncoding,
                ["rule"] = RuleReference(span.Rule),
            };
            if (span.Matched is not null) item["matched"] = span.Matched;
            values.Add(item);
        }
        return values;
    }

    private static object RuleReference(RuleReference rule)
    {
        var artifact = new Dictionary<string, object?>
        {
            ["kind"] = rule.Artifact.Kind.ToWire(),
            ["id"] = rule.Artifact.Id,
            ["version"] = rule.Artifact.Version,
        };
        if (rule.Artifact.Digest is not null) artifact["digest"] = rule.Artifact.Digest;
        var value = new Dictionary<string, object?>
        {
            ["artifact"] = artifact,
            ["ruleId"] = rule.RuleId,
            ["ruleDigest"] = rule.RuleDigest,
        };
        if (rule.Source is not null)
        {
            var source = new Dictionary<string, object?>
            {
                ["providerId"] = rule.Source.ProviderId,
                ["reproducibility"] = rule.Source.Reproducibility.ToWire(),
            };
            if (rule.Source.SnapshotId is not null) source["snapshotId"] = rule.Source.SnapshotId;
            value["source"] = source;
        }
        return value;
    }
}
