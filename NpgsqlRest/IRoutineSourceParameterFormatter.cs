using Npgsql;

namespace NpgsqlRest
{
    public interface IRoutineSourceParameterFormatter
    {
        /// <summary>
        /// Return true to call FormatCommand
        /// Return false to call AppendCommandParameter or AppendEmpty (when no parameters are present).
        /// </summary>
        bool IsFormattable { get; }

        /// <summary>
        /// Return true to call format methods with HttpContext reference.
        /// Return false to call format methods without HttpContext reference.
        /// </summary>
        bool RefContext { get => false; }

        /// <summary>
        /// Appends result to the command expression string.
        /// </summary>
        /// <param name="parameter">NpgsqlRestParameter extended parameter with actual name and type descriptor</param>
        /// <param name="index">index of the current parameter</param>
        /// <returns>string to append to expression or null to skip (404 if endpoint is not handled in the next handler)</returns>
        string? AppendCommandParameter(NpgsqlRestParameter parameter, int index) => null;

        /// <summary>
        /// Formats the command expression string.
        /// </summary>
        /// <param name="routine">Current routine data</param>
        /// <param name="parameters">Extended parameters list.</param>
        /// <returns>expression string or null to skip (404 if endpoint is not handled in the next handler)</returns>
        string? FormatCommand(Routine routine, NpgsqlParameterCollection parameters) => null;

        /// <summary>
        /// Called when there are no parameters to append.
        /// </summary>
        /// <returns>string to append to expression or null to skip (404 if endpoint is not handled in the next handler)</returns>
        string? AppendEmpty() => null;

        /// <summary>
        /// Appends result to the command expression string.
        /// </summary>
        /// <param name="parameter">NpgsqlRestParameter extended parameter with actual name and type descriptor</param>
        /// <param name="index">index of the current parameter</param>
        /// <param name="context">HTTP context reference</param>
        /// <returns>string to append to expression or null to skip (404 if endpoint is not handled in the next handler)</returns>
        string? AppendCommandParameter(NpgsqlRestParameter parameter, int index, HttpContext context) => null;

        /// <summary>
        /// Formats the command expression string.
        /// </summary>
        /// <param name="routine">Current routine data</param>
        /// <param name="parameters">Extended parameters list.</param>
        /// <param name="context">HTTP context reference</param>
        /// <returns>expression string or null to skip (404 if endpoint is not handled in the next handler)</returns>
        string? FormatCommand(Routine routine, NpgsqlParameterCollection parameters, HttpContext context) => null;

        /// <summary>
        /// Called when there are no parameters to append.
        /// </summary>
        /// <param name="context">HTTP context reference</param>
        /// <returns>string to append to expression or null to skip (404 if endpoint is not handled in the next handler)</returns>
        string? AppendEmpty(HttpContext context) => null;
    }
}