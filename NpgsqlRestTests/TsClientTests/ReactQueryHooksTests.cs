using NpgsqlRest.TsClient;

namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientReactQueryHooksTests()
        {
            script.Append("""
create schema if not exists tsclient_test;

-- url-only endpoints emit only a URL builder, so they must not appear in the hooks file.
create function tsclient_test.hooks_url_only(_id int)
returns int
language sql
as $$
select _id;
$$;
comment on function tsclient_test.hooks_url_only(int) is '
tsclient_module=hooks_url_only
HTTP GET
tsclient_url_only=true
';

-- tsclient_hooks=off excludes the routine from the hooks file only; the client function
-- is still generated.
create function tsclient_test.hooks_opt_out(_x int)
returns int
language sql
as $$
select _x;
$$;
comment on function tsclient_test.hooks_opt_out(int) is '
tsclient_module=hooks_opt_out
HTTP GET
tsclient_hooks=off
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class ReactQueryHooksTests
    {
        // 1. GET routine with parameters: useQuery hook + exported key factory with all/byRequest.
        private const string ExpectedGetProductHooks = """
import {
    useQuery,
    useMutation,
    type UseQueryOptions,
    type UseMutationOptions,
} from "@tanstack/react-query";
import * as api from "../get_product";

type CategoriesCategoryIdProductsProductIdRequest = Parameters<typeof api.categoriesCategoryIdProductsProductId>[0];
type CategoriesCategoryIdProductsProductIdResult = Awaited<ReturnType<typeof api.categoriesCategoryIdProductsProductId>>;

export const categoriesCategoryIdProductsProductIdKeys = {
    all: ["categoriesCategoryIdProductsProductId"] as const,
    byRequest: (request: CategoriesCategoryIdProductsProductIdRequest) =>
        ["categoriesCategoryIdProductsProductId", request] as const,
};

export function useCategoriesCategoryIdProductsProductId(
    request: CategoriesCategoryIdProductsProductIdRequest,
    options?: Omit<
        UseQueryOptions<CategoriesCategoryIdProductsProductIdResult>,
        "queryKey" | "queryFn"
    >,
) {
    return useQuery({
        queryKey: categoriesCategoryIdProductsProductIdKeys.byRequest(request),
        queryFn: () => api.categoriesCategoryIdProductsProductId(request),
        ...options,
    });
}

""";

        [Fact]
        public void Test_QueryHook_WithParams_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientHooksOutputPath, "hooks", "get_productHooks.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedGetProductHooks);
        }

        // 2. POST routine: useMutation hook, no key factory.
        private const string ExpectedAddNumbersHooks = """
import {
    useQuery,
    useMutation,
    type UseQueryOptions,
    type UseMutationOptions,
} from "@tanstack/react-query";
import * as api from "../add_numbers";

type TsclientTestAddNumbersRequest = Parameters<typeof api.tsclientTestAddNumbers>[0];
type TsclientTestAddNumbersResult = Awaited<ReturnType<typeof api.tsclientTestAddNumbers>>;

export function useTsclientTestAddNumbersMutation(
    options?: Omit<
        UseMutationOptions<TsclientTestAddNumbersResult, unknown, TsclientTestAddNumbersRequest>,
        "mutationFn"
    >,
) {
    return useMutation({
        mutationFn: (request: TsclientTestAddNumbersRequest) => api.tsclientTestAddNumbers(request),
        ...options,
    });
}

""";

        [Fact]
        public void Test_MutationHook_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientHooksOutputPath, "hooks", "add_numbersHooks.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedAddNumbersHooks);
            content.Should().NotContain("Keys = {", "mutations must not emit key factories");
        }

        // 3. Parameterless GET: key factory with `all` only, hook without a request argument.
        private const string ExpectedGetHelloHooks = """
import {
    useQuery,
    useMutation,
    type UseQueryOptions,
    type UseMutationOptions,
} from "@tanstack/react-query";
import * as api from "../get_hello";

type TsclientTestGetHelloResult = Awaited<ReturnType<typeof api.tsclientTestGetHello>>;

export const tsclientTestGetHelloKeys = {
    all: ["tsclientTestGetHello"] as const,
};

export function useTsclientTestGetHello(
    options?: Omit<
        UseQueryOptions<TsclientTestGetHelloResult>,
        "queryKey" | "queryFn"
    >,
) {
    return useQuery({
        queryKey: tsclientTestGetHelloKeys.all,
        queryFn: () => api.tsclientTestGetHello(),
        ...options,
    });
}

""";

        [Fact]
        public void Test_QueryHook_Parameterless_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientHooksOutputPath, "hooks", "get_helloHooks.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedGetHelloHooks);
            content.Should().NotContain("byRequest", "parameterless queries only get the `all` key");
        }

        // 4 + 5. QueryKeyPrefix as the first key element; ExposeQueryKeys=false inlines the key
        // expressions and exports no factories.
        private const string ExpectedPrefixedGetProductHooks = """
import {
    useQuery,
    useMutation,
    type UseQueryOptions,
    type UseMutationOptions,
} from "~/lib/query";
import * as api from "../get_product";

type CategoriesCategoryIdProductsProductIdRequest = Parameters<typeof api.categoriesCategoryIdProductsProductId>[0];
type CategoriesCategoryIdProductsProductIdResult = Awaited<ReturnType<typeof api.categoriesCategoryIdProductsProductId>>;

export function useCategoriesCategoryIdProductsProductId(
    request: CategoriesCategoryIdProductsProductIdRequest,
    options?: Omit<
        UseQueryOptions<CategoriesCategoryIdProductsProductIdResult>,
        "queryKey" | "queryFn"
    >,
) {
    return useQuery({
        queryKey: ["testApi", "categoriesCategoryIdProductsProductId", request] as const,
        queryFn: () => api.categoriesCategoryIdProductsProductId(request),
        ...options,
    });
}

""";

        [Fact]
        public void Test_QueryKeyPrefix_And_NoExposedKeys_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientHooksPrefixOutputPath, "hooks", "get_productHooks.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedPrefixedGetProductHooks);
            content.Should().Contain("queryKey: [\"testApi\", ", "QueryKeyPrefix must be the first key element");
            content.Should().NotContain("Keys = {", "ExposeQueryKeys=false must not export key factories");
            content.Should().Contain("} from \"~/lib/query\";", "ImportFrom must replace the @tanstack/react-query specifier");
            content.Should().NotContain("@tanstack/react-query", "the default specifier must not appear when ImportFrom is set");
        }

        // 6. SSE, upload and url-only routines are absent from the hooks output.
        [Fact]
        public void Test_SseUploadUrlOnly_ExcludedFromHooks()
        {
            var sse = Path.Combine(Setup.Program.TsClientHooksOutputPath, "hooks", "sse_endpointHooks.ts");
            File.Exists(sse).Should().BeFalse($"SSE endpoints must not produce hooks, but found {sse}");

            var upload = Path.Combine(Setup.Program.TsClientHooksOutputPath, "hooks", "upload_fileHooks.ts");
            File.Exists(upload).Should().BeFalse($"upload endpoints must not produce hooks, but found {upload}");

            var urlOnly = Path.Combine(Setup.Program.TsClientHooksOutputPath, "hooks", "hooks_url_onlyHooks.ts");
            File.Exists(urlOnly).Should().BeFalse($"url-only endpoints must not produce hooks, but found {urlOnly}");
        }

        // 7. tsclient_hooks=off: absent from hooks output, present in the client output.
        [Fact]
        public void Test_HooksOptOut_ExcludedFromHooks_PresentInClient()
        {
            var hooksFile = Path.Combine(Setup.Program.TsClientHooksOutputPath, "hooks", "hooks_opt_outHooks.ts");
            File.Exists(hooksFile).Should().BeFalse($"tsclient_hooks=off must exclude the routine from hooks, but found {hooksFile}");

            var clientFile = Path.Combine(Setup.Program.TsClientHooksOutputPath, "hooks_opt_out.ts");
            File.Exists(clientFile).Should().BeTrue($"the client function must still be generated at {clientFile}");
            File.ReadAllText(clientFile).Should().Contain("export async function tsclientTestHooksOptOut");
        }

        // 8. Enabled without FilePath fails fast when the handler is constructed (startup time).
        [Fact]
        public void Test_EnabledWithoutFilePath_FailsFastAtConstruction()
        {
            var act = () => new TsClient(new TsClientOptions
            {
                FilePath = Path.Combine(Path.GetTempPath(), "never-used.ts"),
                ReactQuery = new TsClientReactQueryOptions { Enabled = true, FilePath = null }
            });

            act.Should().Throw<ArgumentException>()
                .WithMessage("*ReactQuery.Enabled is true but ReactQuery.FilePath is not set*")
                .WithMessage("*{0}*");
        }

        // 9. Enabled with an empty ImportFrom fails fast when the handler is constructed.
        [Fact]
        public void Test_EnabledWithEmptyImportFrom_FailsFastAtConstruction()
        {
            var act = () => new TsClient(new TsClientOptions
            {
                FilePath = Path.Combine(Path.GetTempPath(), "never-used.ts"),
                ReactQuery = new TsClientReactQueryOptions
                {
                    Enabled = true,
                    FilePath = Path.Combine(Path.GetTempPath(), "never-usedHooks.ts"),
                    ImportFrom = ""
                }
            });

            act.Should().Throw<ArgumentException>()
                .WithMessage("*ReactQuery.ImportFrom cannot be null or empty*")
                .WithMessage("*@tanstack/react-query*");
        }
    }
}
