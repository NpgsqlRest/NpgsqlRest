namespace NpgsqlRest;

public static class Consts
{
    public const string True = "true";
    public const string False = "false";
    public const string Null = "null";
    public const char DoubleQuote = '"';
    public const string DoubleQuoteColon = "\":";
    public const char OpenParenthesis = '(';
    public const char CloseParenthesis = ')';
    public const char Comma = ',';
    public const char Dot = '.';
    public const char Backslash = '\\';
    public const char Colon = ':';
    public const char Equal = '=';
    public const char OpenBrace = '{';
    public const char CloseBrace = '}';
    public const char OpenBracket = '[';
    public const char CloseBracket = ']';
    public const char Space = ' ';
    public const string DoubleColon = "::";
    public const string FirstParam = "$1";
    public const string FirstNamedParam = "=>$1";
    public const string CloseParenthesisStr = ")";
    public const char Dollar = '$';
    public const string NamedParam = "=>$";
    public const string OpenRow = "=>row(";
    public const string CloseRow = ")::";
    public const char At = '@';
    public const char Multiply = '*';
    public const char Question = '?';
    public const string EmptyArray = "[]";
    public const string EmptyObj = "{}";
    public const string SetContext = "select set_config($1,$2,false)";
}
