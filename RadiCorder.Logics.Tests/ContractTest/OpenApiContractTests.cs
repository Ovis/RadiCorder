using System.Text.Json;

namespace RadiCorder.Logics.Tests.ContractTest;

/// <summary>
/// OpenAPI 契約テスト
/// </summary>
public class OpenApiContractTests
{
    /// <summary>
    /// OpenAPI ドキュメントが存在し、3.x 形式で出力されることを確認する。
    /// </summary>
    [Test]
    public void OpenApiJson_存在して_バージョンが3系である()
    {
        var document = LoadOpenApiDocument();

        var openApiVersion = document.RootElement.GetProperty("openapi").GetString();

        Assert.That(openApiVersion, Is.Not.Null.And.StartsWith("3."));
    }

    /// <summary>
    /// 主要機能の API パスが OpenAPI に含まれていることを確認する。
    /// </summary>
    [Test]
    public void OpenApiJson_主要機能パスを含む()
    {
        var document = LoadOpenApiDocument();
        var pathsElement = document.RootElement.GetProperty("paths");

        var requiredPaths = new[]
        {
            "/api/programs/search",
            "/api/programs/reserve",
            "/api/reserves/keywords",
            "/api/recordings",
            "/api/recordings/play/{recordId}",
            "/api/recordings/download/{recordId}",
            "/api/recordings/tags/bulk-add",
            "/api/settings/record-directory",
            "/api/settings/duration",
            "/api/settings/external-import/scan",
            "/api/settings/external-import/save"
        };

        foreach (var path in requiredPaths)
        {
            Assert.That(pathsElement.TryGetProperty(path, out _), Is.True, $"OpenAPI paths に {path} がありません。");
        }
    }

    /// <summary>
    /// 主要契約スキーマが OpenAPI に含まれていることを確認する。
    /// </summary>
    [Test]
    public void OpenApiJson_主要契約スキーマを含む()
    {
        var document = LoadOpenApiDocument();
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        var requiredSchemas = new[]
        {
            "ProgramSearchEntity",
            "ReserveEntryRequest",
            "KeywordReserveEntry",
            "RecordingBulkDeleteRequest",
            "RecordingBulkTagRequest",
            "UpdateDurationEntity",
            "ExternalImportScanRequest",
            "ExternalImportCandidateEntry"
        };

        foreach (var schema in requiredSchemas)
        {
            Assert.That(schemas.TryGetProperty(schema, out _), Is.True, $"OpenAPI schemas に {schema} がありません。");
        }
    }

    /// <summary>
    /// OpenAPI JSON を読み込む。
    /// </summary>
    private static JsonDocument LoadOpenApiDocument()
    {
        var openApiPath = FindOpenApiPath();
        var json = File.ReadAllText(openApiPath);
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// テスト実行ディレクトリからリポジトリを辿って OpenAPI ファイルを探索する。
    /// </summary>
    private static string FindOpenApiPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "RadiCorder", "openapi", "openapi.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("OpenAPIファイルが見つかりませんでした。", "RadiCorder/openapi/openapi.json");
    }
}
