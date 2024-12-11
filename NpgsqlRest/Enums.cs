﻿namespace NpgsqlRest;

public enum RoutineType { Table, View, Function, Procedure, Other }
public enum CrudType { Select, Insert, Update, Delete }
public enum Method { GET, PUT, POST, DELETE, HEAD, OPTIONS, TRACE, PATCH, CONNECT }
public enum RequestParamType { QueryString, BodyJson }
public enum ParamType { QueryString, BodyJson, BodyParam, HeaderParam }
public enum CommentHeader { None, Simple, Full }
public enum TextResponseNullHandling { EmptyString, NullLiteral, NoContent }
public enum QueryStringNullHandling { EmptyString, NullLiteral, Ignore }

public enum ServiceProviderObject
{
    /// <summary>
    /// Connection is not provided in service provider. Connection is supplied either by ConnectionString or by DataSource option.
    /// </summary>
    None,
    /// <summary>
    /// NpgsqlRest attempts to get NpgsqlDataSource from service provider (assuming one is provided).
    /// </summary>
    NpgsqlDataSource,
    /// <summary>
    /// NpgsqlRest attempts to get NpgsqlConnection from service provider (assuming one is provided).
    /// </summary>
    NpgsqlConnection
}

public enum CommentsMode 
{ 
    /// <summary>
    /// Routine comments are ignored.
    /// </summary>
    Ignore,
    /// <summary>
    /// Creates all endpoints and parses comments for to configure endpoint meta data.
    /// </summary>
    ParseAll,
    /// <summary>
    /// Creates only endpoints from routines containing a comment with HTTP tag and and configures endpoint meta data.
    /// </summary>
    OnlyWithHttpTag
}

public enum RequestHeadersMode
{
    /// <summary>
    /// Ignore request headers, don't send them to PostgreSQL (default).
    /// </summary>
    Ignore,
    /// <summary>
    /// Send all request headers as json object to PostgreSQL by executing set_config('context.headers', headers, false) before routine call.
    /// </summary>
    Context,
    /// <summary>
    /// Send all request headers as json object to PostgreSQL as default routine parameter with name set by RequestHeadersParameterName option.
    /// This parameter has to have the default value (null) in the routine and have to be text or json type.
    /// </summary>
    Parameter
}