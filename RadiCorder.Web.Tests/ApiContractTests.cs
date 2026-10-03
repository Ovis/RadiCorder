using System.Text.Json.Nodes;

namespace RadiCorder.Web.Tests;

public class ApiContractTests
{
    [Test]
    public async Task OpenApi_既存の全API契約と一致する()
    {
        await using var host = new WebTestHost();
        await host.StartAsync();
        var json = JsonNode.Parse(await host.Client.GetStringAsync("/openapi/v1.json"))!;
        json.AsObject().Remove("servers");
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "api-baseline.json")));
        Assert.That(JsonNode.DeepEquals(json, expected), Is.True, "公開APIのpath、schema、入力と応答を維持すること");
    }
}
