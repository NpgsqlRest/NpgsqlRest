namespace NpgsqlRest
{
    public interface IEndpointCreateHandler
    {
        /// <summary>
        /// Before creating endpoints.
        /// </summary>
        /// <param name="builder">current application builder</param>
        /// <param name="options">current NpgsqlRest options</param>
        void Setup(IApplicationBuilder builder, NpgsqlRestOptions options) {  }

        /// <summary>
        /// Called by the core comment parser, within its single parse pass, for each comment line
        /// that core did NOT recognize as a built-in directive. Lets a plugin claim its own
        /// annotations (e.g. <c>openapi …</c>, <c>mcp …</c>) without a second pass over the comment.
        /// <para>
        /// If this plugin owns the line, apply it (typically by storing into
        /// <see cref="RoutineEndpoint.Items"/>) and return a <see cref="CommentLineResult"/> with a
        /// short label — core logs it centrally (consistent with built-in annotation logging) and
        /// stops offering the line to other handlers. Return <c>null</c> if the line is not this
        /// plugin's annotation; it is then offered to the next handler, and finally surfaced via
        /// <see cref="RoutineEndpoint.UnhandledCommentLines"/> (prose) if no handler claims it.
        /// </para>
        /// <paramref name="words"/> / <paramref name="wordsLower"/> are pre-tokenized by core (split
        /// on space/comma; the lower-cased variant for keyword matching) so handlers do not re-tokenize.
        /// <para>
        /// Set <see cref="CommentLineResult.RequestsEndpoint"/> to true when the annotation means the
        /// routine should be exposed as an endpoint (e.g. <c>mcp</c>) — under
        /// <see cref="CommentsMode.OnlyAnnotated"/> this lets a routine with no HTTP tag still be
        /// created. Leave it false for pure modifiers (e.g. <c>openapi hide</c>), which must NOT by
        /// themselves cause an endpoint to be created.
        /// </para>
        /// </summary>
        CommentLineResult? HandleCommentLine(RoutineEndpoint endpoint, string line, string[] words, string[] wordsLower) => null;

        /// <summary>
        /// After successful endpoint creation.
        /// </summary>
        void Handle(RoutineEndpoint endpoint) { }

        /// <summary>
        /// After all endpoints are created.
        /// </summary>
        void Cleanup(RoutineEndpoint[] endpoints) {  }

        /// <summary>
        /// After all endpoints are created.
        /// </summary>
        void Cleanup() { }
    }

    /// <summary>
    /// Result of <see cref="IEndpointCreateHandler.HandleCommentLine"/> when a plugin claims a comment
    /// line. <c>Label</c> is a short description logged centrally by core (consistent with built-in
    /// annotation logging). <c>RequestsEndpoint</c> = true signals the routine should be created as an
    /// endpoint even without an HTTP tag (exposure intent); false = modifier only.
    /// </summary>
    public readonly record struct CommentLineResult(string Label, bool RequestsEndpoint = false);
}
