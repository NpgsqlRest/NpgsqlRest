create table bench_items (id int primary key, name text not null, price numeric not null, qty int not null);
insert into bench_items select i, 'item ' || i, i * 1.5, i % 17 from generate_series(1, 1000) i;
analyze bench_items;

create function benchmark_get_text() returns text language sql stable as $$ select 'Hello, World!' $$;

create function benchmark_get_rows(_count int)
returns table (id int, name text, active boolean, amount numeric, created_at timestamptz, tags text[], data jsonb)
language sql stable as $$
select i, 'name ' || i, i % 2 = 0, i * 1.25, timestamptz '2026-01-01 00:00:00+00' + make_interval(mins => i),
       array['a' || i, 'b' || i], jsonb_build_object('id', i, 'label', 'item ' || i)
from generate_series(1, _count) as i $$;

create function benchmark_get_json(_count int) returns json language sql stable as $$
select json_agg(json_build_object('id', i, 'name', 'name ' || i, 'description', repeat('x', 40), 'value', i * 1.5))
from generate_series(1, _count) i $$;

create function benchmark_plpgsql(_id int) returns text language plpgsql stable as $$
declare _name text; _price numeric; _qty int; _count int; _max int;
begin
  select name into _name from bench_items where id = _id;
  select price into _price from bench_items where id = _id;
  select qty into _qty from bench_items where id = _id;
  select count(*) into _count from bench_items where qty = _qty;
  select max(id) into _max from bench_items where id < _id;
  return _name || ' ' || _price || ' ' || _qty || ' ' || _count || ' ' || coalesce(_max, 0);
end $$;

create function ctx_text() returns text language sql stable as $$
select 'Hello, ' || coalesce(nullif(current_setting('request.ip_address', true), ''), 'World') || '!' $$;

create function ctx_rows(_count int)
returns table (id int, name text, active boolean, amount numeric, created_at timestamptz, tags text[], data jsonb)
language sql stable as $$
select i, 'name ' || i, i % 2 = 0, i * 1.25, timestamptz '2026-01-01 00:00:00+00' + make_interval(mins => i),
       array['a' || i, 'b' || i], jsonb_build_object('id', i, 'ip', current_setting('request.ip_address', true))
from generate_series(1, _count) as i $$;

create function benchmark_post(_a int, _b text, _c boolean, _d numeric) returns text language sql stable as $$
select _a || ' ' || _b || ' ' || _c || ' ' || _d $$;

comment on function benchmark_get_text() is 'HTTP GET';
comment on function benchmark_get_rows(int) is 'HTTP GET';
comment on function benchmark_get_json(int) is 'HTTP GET';
comment on function benchmark_plpgsql(int) is 'HTTP GET';
comment on function benchmark_post(int, text, boolean, numeric) is 'HTTP POST';
comment on function ctx_text() is 'HTTP GET
user_context';
comment on function ctx_rows(int) is 'HTTP GET
user_context';
