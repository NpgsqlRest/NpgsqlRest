using System.Globalization;
using System.Text;
using Npgsql;

namespace NpgsqlRest;

public class RoutineSourceParameterFormatter : IRoutineSourceParameterFormatter
{
    public bool IsFormattable { get; } = false;

    public string AppendCommandParameter(NpgsqlRestParameter parameter, int index)
    {
        var suffix = parameter.TypeDescriptor.IsCastToText() ?
            string.Concat(Consts.DoubleColon, parameter.TypeDescriptor.OriginalType) :
            string.Empty;

        if (index == 0)
        {
            return parameter.OriginalName is null ?
                string.Concat(Consts.FirstParam, suffix) :
                string.Concat(parameter.OriginalName, Consts.FirstNamedParam, suffix);
        }

        var indexStr = (index + 1).ToString(CultureInfo.InvariantCulture);

        return parameter.OriginalName is null ?
            string.Concat(Consts.Comma, Consts.Dollar, indexStr, suffix) :
            string.Concat(Consts.Comma, parameter.OriginalName, Consts.NamedParam, indexStr, suffix);
    }

    public string? AppendEmpty() => Consts.CloseParenthesisStr;
}

public class RoutineSourceCustomTypesParameterFormatter : IRoutineSourceParameterFormatter
{
    public bool IsFormattable { get; } = true;

    public string FormatCommand(Routine routine, NpgsqlParameterCollection parameters)
    {
        var sb = new StringBuilder(routine.Expression, routine.Expression.Length + parameters.Count * 20);
        var count = parameters.Count;

        var culture = CultureInfo.InvariantCulture;

        for (var i = 0; i < count; i++)
        {
            var parameter = (NpgsqlRestParameter)parameters[i];
            var typeDescriptor = parameter.TypeDescriptor;

            var suffix = typeDescriptor.IsCastToText() ?
                string.Concat(Consts.DoubleColon, typeDescriptor.OriginalType) :
                string.Empty;

            if (i > 0)
            {
                sb.Append(Consts.Comma);
            }

            var indexStr = (i + 1).ToString(culture);

            if (typeDescriptor.CustomType is null)
            {
                if (parameter.OriginalName is null)
                {
                    sb.Append(Consts.Dollar)
                      .Append(indexStr)
                      .Append(suffix);
                }
                else
                {
                    sb.Append(parameter.OriginalName)
                      .Append(Consts.NamedParam)
                      .Append(indexStr);
                }
            }
            else
            {
                if (typeDescriptor.CustomTypePosition == 1)
                {
                    sb.Append(typeDescriptor.OriginalParameterName)
                      .Append(Consts.OpenRow);
                }

                sb.Append(Consts.Dollar)
                  .Append(indexStr)
                  .Append(suffix);

                if (i == count - 1 ||
                    typeDescriptor.CustomTypePosition !=
                    ((NpgsqlRestParameter)parameters[i + 1]).TypeDescriptor.CustomTypePosition - 1)
                {
                    sb.Append(Consts.CloseRow)
                      .Append(typeDescriptor.CustomType);
                }
            }
        }

        sb.Append(Consts.CloseParenthesis);
        return sb.ToString();
    }
}