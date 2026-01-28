namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: validate | validation
    /// Syntax: validate _param_name using rule_name[, rule_name2, ...]
    ///
    /// Description: Add validation rule(s) to a parameter.
    /// The parameter name can be either the original PostgreSQL name (e.g., _email) or the converted name (e.g., email).
    /// The rule_name must match a key in ValidationOptions.Rules dictionary.
    /// Multiple rules can be specified as comma-separated values: validate _email using required, email
    /// Multiple validate annotations can also be added for the same parameter on separate lines.
    /// </summary>
    private static readonly string[] ValidateKey = [
        "validate",
        "validation",
    ];

    private static void HandleValidate(
        Routine routine,
        RoutineEndpoint endpoint,
        string[] words,
        int len,
        string description)
    {
        // Syntax: validate _param_name using rule_name[, rule_name2, ...]
        // Minimum: validate _param using rule = 4 words
        if (len < 4)
        {
            Logger?.ValidationInvalidSyntax(description, "Expected: validate _param_name using rule_name[, rule_name2, ...]");
            return;
        }

        var paramName = words[1];

        // Check for "using" keyword
        if (!string.Equals(words[2], "using", StringComparison.OrdinalIgnoreCase))
        {
            Logger?.ValidationInvalidSyntax(description, $"Expected 'using' keyword, got '{words[2]}'");
            return;
        }

        // Verify parameter exists in the routine (check both original and converted names)
        if (!routine.OriginalParamsHash.Contains(paramName) && !routine.ParamsHash.Contains(paramName))
        {
            Logger?.ValidationParameterNotFound(description, paramName);
            return;
        }

        // Find the actual parameter to get both names
        var param = routine.Parameters.FirstOrDefault(x =>
            string.Equals(x.ActualName, paramName, StringComparison.Ordinal) ||
            string.Equals(x.ConvertedName, paramName, StringComparison.Ordinal));

        if (param is null)
        {
            Logger?.ValidationParameterNotFound(description, paramName);
            return;
        }

        // Get the original parameter name for storage
        var originalName = param.ActualName;

        // words[3..] contains the rule names - already split by SplitWords (which splits by both space and comma)
        // e.g., "validate _email using required, email" -> words = ["validate", "_email", "using", "required", "email"]
        // So words[3..] = ["required", "email"]
        foreach (var ruleName in words[3..])
        {
            // Look up the validation rule
            if (!Options.ValidationOptions.Rules.TryGetValue(ruleName, out var rule))
            {
                Logger?.ValidationRuleNotFound(description, ruleName);
                continue;
            }

            // Store validation using the original parameter name as the key
            endpoint.ParameterValidations ??= new Dictionary<string, List<ValidationRule>>();
            if (!endpoint.ParameterValidations.TryGetValue(originalName, out var rules))
            {
                rules = new List<ValidationRule>();
                endpoint.ParameterValidations[originalName] = rules;
            }
            rules.Add(rule);

            CommentLogger?.ValidationRuleSet(description, paramName, ruleName);
        }
    }
}
