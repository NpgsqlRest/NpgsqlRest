namespace NpgsqlRest
{
    public class NpgsqlRestMetadataEntry
    {
        internal NpgsqlRestMetadataEntry(RoutineEndpoint endpoint, IRoutineSourceParameterFormatter formatter, string key)
        {
            Endpoint = endpoint;
            Formatter = formatter;
            Key = key;
        }
        public RoutineEndpoint Endpoint { get; }
        public IRoutineSourceParameterFormatter Formatter { get; }
        public string Key { get; }
    }
}