namespace NpgsqlRest
{
    public interface IEndpointCreateHandler
    {
        /// <summary>
        /// Before creating endpoints.
        /// </summary>
        /// <param name="builder">current application builder</param>
        /// <param name="logger">configured application logger</param>
        /// <param name="options">current NpgsqlRest options</param>
        void Setup(IApplicationBuilder builder, ILogger? logger, NpgsqlRestOptions options) {  }

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
}