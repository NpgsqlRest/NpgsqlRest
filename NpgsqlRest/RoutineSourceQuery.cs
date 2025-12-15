namespace NpgsqlRest;

internal class RoutineSourceQuery
{
    public const string Query = """
    with _types as (

        select
            (quote_ident(n.nspname) || '.' || quote_ident(t.typname))::regtype::text as name,
            a.attnum as att_pos,
            quote_ident(a.attname) as att_name,
            pg_catalog.format_type(a.atttypid, a.atttypmod) as att_type
        from 
            pg_catalog.pg_type t
            join pg_catalog.pg_namespace n on n.oid = t.typnamespace
            join pg_catalog.pg_class c on t.typrelid = c.oid and c.relkind in ('r', 'c')
            join pg_catalog.pg_attribute a on t.typrelid = a.attrelid and a.attisdropped is false
        where
            nspname not like 'pg_%'
            and nspname <> 'information_schema'
            and has_schema_privilege(current_user, n.nspname, 'USAGE')
            and a.attnum > 0

    ), _schemas as (

        select
            array_agg(nspname) as schemas
        from
            pg_catalog.pg_namespace
        where
            nspname not like 'pg_%'
            and nspname <> 'information_schema'
            and ($1 is null or nspname similar to $1)
            and ($2 is null or nspname not similar to $2)
            and ($3 is null or nspname = any($3))
            and ($4 is null or not nspname = any($4))

    ), _routines as (

        select
            r.routine_type, 
            r.specific_schema, 
            r.specific_name,
            r.routine_name,
            r.data_type, 
            r.type_udt_schema,
            r.type_udt_name,
            (r.type_udt_schema || '.' || r.type_udt_name)::regtype::text as return_type,
            r.data_type = 'USER-DEFINED' as is_user_defined,
            quote_ident(p.parameter_name::text) as param_name,
            p.parameter_mode = 'IN' or p.parameter_mode = 'INOUT' as is_in_param,
            p.parameter_mode = 'INOUT' or p.parameter_mode = 'OUT' as is_out_param,
            row_number() over (partition by r.specific_schema, r.specific_name order by p.ordinal_position, t.att_pos) as param_position,
            case when p.data_type = 'bit' then 'varbit' else (p.udt_schema || '.' || p.udt_name)::regtype::text end as param_type,
            t.att_name as custom_type_name,
            t.att_type as custom_type_type,
            t.att_pos as custom_type_pos
        from information_schema.routines r
        join _schemas on r.specific_schema = any(_schemas.schemas)
        left join information_schema.parameters p on r.specific_name = p.specific_name and r.specific_schema = p.specific_schema
        left join _types t on 
            p.data_type = 'USER-DEFINED' 
            and (p.udt_schema || '.' || p.udt_name)::regtype::text = t.name 
        where
            coalesce(r.type_udt_name, '') <> 'trigger'
            and r.routine_type in ('FUNCTION', 'PROCEDURE')
            and ($5 is null or r.routine_name similar to $5)
            and ($6 is null or r.routine_name not similar to $6)
            and ($7 is null or r.routine_name = any($7))
            and ($8 is null or not r.routine_name = any($8))
            and ($9 is null or lower(r.external_language) = any($9))
            and ($10 is null or lower(r.external_language) = any($10) is false)
        order by 
            r.specific_schema, r.specific_name, p.ordinal_position, t.att_pos

    ), _routine_aggs as (

        select
            lower(r.routine_type) as type,
            quote_ident(r.specific_schema) as schema,
            quote_ident(r.routine_name) as name,
            proc.oid as oid,
            r.specific_name,
            des.description as comment,
            proc.proisstrict as is_strict,
            proc.provolatile as volatility_option,
            proc.proretset as returns_set,
            r.return_type,
            coalesce(array_agg(r.param_name order by r.param_position) filter(where r.is_in_param), '{}'::text[]) as in_params,
            coalesce((array_agg(r.param_name order by r.param_position) filter(where r.is_out_param)),'{}'::text[]) as out_params,
            coalesce(array_agg(r.param_type order by r.param_position) filter(where r.is_in_param), '{}'::text[]) as in_param_types,
            coalesce((array_agg(r.param_type order by r.param_position) filter(where r.is_out_param)), '{}'::text[]) as out_param_types,
            pg_get_function_arguments(proc.oid) as arguments_def,
            case when proc.provariadic <> 0 then true else false end as has_variadic,
            r.type_udt_schema,
            r.type_udt_name,
            r.data_type,
            coalesce(array_agg(r.custom_type_name order by r.param_position) filter(where r.is_in_param), '{}'::text[]) as custom_param_type_names,
            coalesce(array_agg(r.custom_type_type order by r.param_position) filter(where r.is_in_param), '{}'::text[]) as custom_param_type_types,
            coalesce(array_agg(r.custom_type_pos order by r.param_position) filter(where r.is_in_param), '{}'::smallint[]) as custom_param_type_positions,
            coalesce(array_agg(r.custom_type_name order by r.param_position) filter(where r.is_out_param), '{}'::text[]) as custom_rec_type_names,
            coalesce(array_agg(r.custom_type_type order by r.param_position) filter(where r.is_out_param), '{}'::text[]) as custom_rec_type_types
        from
            _routines r
            join pg_catalog.pg_proc proc on r.specific_name = proc.proname || '_' || proc.oid
            left join pg_catalog.pg_description des on proc.oid = des.objoid
        group by
            r.routine_type, 
            r.specific_schema, 
            r.routine_name,
            r.specific_name, 
            r.data_type, 
            r.type_udt_schema, 
            r.type_udt_name, 
            r.return_type, 
            r.is_user_defined,
            des.description,
            proc.oid,
            proc.proisstrict, 
            proc.procost, 
            proc.prorows, 
            proc.proparallel, 
            proc.provolatile,
            proc.proretset, 
            proc.provariadic
    )
    select
        type,
        r1.schema,
        r1.name,
        comment,
        is_strict,
        volatility_option,
        returns_set,
        case when custom_types.col_name is not null then 'record' else coalesce(return_type, 'void') end as return_type,

        array_length(coalesce(custom_types.col_name, case when array_length(r1.out_params, 1) is null then array[r1.name]::text[] else r1.out_params end), 1) as return_record_count,
        coalesce(custom_types.col_name, case when array_length(r1.out_params, 1) is null then array[r1.name]::text[] else r1.out_params end) as return_record_names,
        coalesce(custom_types.col_type, case when array_length(r1.out_param_types, 1) is null then array[return_type]::text[] else r1.out_param_types end) as return_record_types,

        coalesce(custom_types.col_type, r1.out_params) = '{}' as is_unnamed_record,
        array_length(in_params, 1) as param_count,
        in_params as param_names,
        in_param_types as param_types,
        arguments_def,
        has_variadic,
        pg_get_functiondef(oid) as definition,
        custom_param_type_names,
        custom_param_type_types,
        custom_param_type_positions,
        case when custom_types.col_name is not null then null else custom_rec_type_names end as custom_rec_type_names,
        case when custom_types.col_name is not null then null else custom_rec_type_types end as custom_rec_type_types
    from _routine_aggs r1
    left join lateral (
        select array_agg(t.att_name order by t.att_pos) as col_name, array_agg(t.att_type order by t.att_pos) as col_type
        from _types t 
        where t.name = r1.return_type
    ) custom_types on true
    """;
}
