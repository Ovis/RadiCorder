using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest
{
    [TestFixture]
    public class RadikoUniqueProcessLogicTests
    {
        private Mock<ILogger<RadikoUniqueProcessLogic>> _loggerMock;
        private Mock<IAppConfigurationService> _configMock;
        private FakeHttpMessageHandler _httpHandler;

        private RadikoUniqueProcessLogic _radikoLogic;

        [SetUp]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<RadikoUniqueProcessLogic>>();
            _configMock = new Mock<IAppConfigurationService>();
            _httpHandler = new FakeHttpMessageHandler();
            var httpClient = new HttpClient(_httpHandler);
            var httpClientFactory = new FakeHttpClientFactory(httpClient);
            _radikoLogic = new RadikoUniqueProcessLogic(_loggerMock.Object, _configMock.Object, httpClientFactory);
            _radikoLogic.InvalidateAuthenticationCache();
        }

        [TearDown]
        public void TearDown()
        {
            _radikoLogic.InvalidateAuthenticationCache();
            _httpHandler.Dispose();
        }


        [Test]
        public async Task GetRadikoAreaAsync_ValidResponse_エリア判定処理テスト()
        {
            _httpHandler.AddHandler(
                req => req.RequestUri!.ToString().Contains("http://radiko.jp/area/"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"document.write('<span class=""JP13"">TOKYO JAPAN</span>');")
                });

            var result = await _radikoLogic.GetRadikoAreaAsync(forceRefresh: true);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Area, Is.EqualTo("JP13"));
        }

        [Test]
        public async Task GetPartialKeyString_キー取得テスト()
        {
            _httpHandler.AddHandler(
                req => req.RequestUri!.ToString().Contains("playerCommon.js"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("new RadikoJSPlayer('a','b','bcd151073c03b352e1ef2fd66c32209da9ca0afa',{")
                });

            var result = await _radikoLogic.GetPartialKeyString();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Key, Is.Not.Null);
            Assert.That(result.Key, Is.EqualTo("bcd151073c03b352e1ef2fd66c32209da9ca0afa"));
        }

        [Test]
        public async Task GetPartialKeyString_取得失敗()
        {
            _httpHandler.AddHandler(
                req => req.RequestUri!.ToString().Contains("playerCommon.js"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("invalid")
                });

            var result = await _radikoLogic.GetPartialKeyString();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Key, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task AuthorizeRadikoAsync_ヘッダー不足_失敗()
        {
            var handler = new FakeHttpMessageHandler();
            handler.AddHandler(
                req => req.RequestUri!.ToString().Contains("auth1"),
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) });
            var logic = new RadikoUniqueProcessLogic(
                _loggerMock.Object,
                _configMock.Object,
                new FakeHttpClientFactory(new HttpClient(handler)));

            var result = await logic.AuthorizeRadikoAsync("session");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Token, Is.EqualTo(string.Empty));
            Assert.That(result.AreaId, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task AuthorizeRadikoAsync_Auth2Out_失敗()
        {
            var handler = new FakeHttpMessageHandler();
            handler.AddHandler(
                req => req.RequestUri!.ToString().Contains("auth1"),
                _ =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Headers.Add("X-Radiko-AuthToken", "token");
                    response.Headers.Add("X-Radiko-KeyLength", "5");
                    response.Headers.Add("X-Radiko-KeyOffset", "0");
                    return response;
                });

            handler.AddHandler(
                req => req.RequestUri!.ToString().Contains("playerCommon.js"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("new RadikoJSPlayer('a','b','abcde',{")
                });

            handler.AddHandler(
                req => req.RequestUri!.ToString().Contains("auth2"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("OUT")
                });

            var logic = new RadikoUniqueProcessLogic(
                _loggerMock.Object,
                _configMock.Object,
                new FakeHttpClientFactory(new HttpClient(handler)));

            var result = await logic.AuthorizeRadikoAsync("session");

            Assert.That(result.IsSuccess, Is.False);
        }

        [Test]
        public async Task AuthorizeRadikoAsync_Auth2にConnectionHeaderを付与する()
        {
            var handler = new FakeHttpMessageHandler();
            handler.AddHandler(
                req => req.RequestUri!.ToString().Contains("auth1"),
                _ =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Headers.Add("X-Radiko-AuthToken", "token");
                    response.Headers.Add("X-Radiko-KeyLength", "5");
                    response.Headers.Add("X-Radiko-KeyOffset", "0");
                    return response;
                });

            handler.AddHandler(
                req => req.RequestUri!.ToString().Contains("playerCommon.js"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("new RadikoJSPlayer('a','b','abcde',{")
                });

            handler.AddHandler(
                req => req.RequestUri!.ToString().Contains("auth2"),
                req =>
                {
                    Assert.That(req.Headers.TryGetValues("X-Radiko-Connection", out var values), Is.True);
                    Assert.That(values!.Single(), Is.EqualTo("wifi"));
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("JP13,0,0\n")
                    };
                });

            var logic = new RadikoUniqueProcessLogic(
                _loggerMock.Object,
                _configMock.Object,
                new FakeHttpClientFactory(new HttpClient(handler)));

            var result = await logic.AuthorizeRadikoAsync("session");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.AreaId, Is.EqualTo("JP13"));
        }

        [Test]
        public async Task AuthorizeRadikoAsync_Auth2のSubStationHeaderを保持する()
        {
            var handler = new FakeHttpMessageHandler();
            handler.AddHandler(
                req => req.RequestUri!.ToString().Contains("auth1"),
                _ =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Headers.Add("X-Radiko-AuthToken", "token");
                    response.Headers.Add("X-Radiko-KeyLength", "5");
                    response.Headers.Add("X-Radiko-KeyOffset", "0");
                    return response;
                });

            handler.AddHandler(
                req => req.RequestUri!.ToString().Contains("playerCommon.js"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("new RadikoJSPlayer('a','b','abcde',{")
                });

            handler.AddHandler(
                req => req.RequestUri!.ToString().Contains("auth2"),
                _ =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("JP27,0,0\n")
                    };
                    response.Headers.Add("x-radiko-substation", "JP27/ABC/ABC1");
                    return response;
                });

            var logic = new RadikoUniqueProcessLogic(
                _loggerMock.Object,
                _configMock.Object,
                new FakeHttpClientFactory(new HttpClient(handler)));

            var result = await logic.AuthorizeRadikoAsync("session");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.SubStations, Is.EqualTo("JP27/ABC/ABC1"));
        }

        [Test]
        public void ResolveSubStationId_一致する組み合わせならSubStationを返す()
        {
            var result = RadikoUniqueProcessLogic.ResolveSubStationId("JP27", "ABC", "JP27/ABC/ABC1,JP13/TBS/TBS1");

            Assert.That(result, Is.EqualTo("ABC1"));
        }

        [Test]
        public void ResolveSubStationId_一致しない組み合わせならnullを返す()
        {
            var result = RadikoUniqueProcessLogic.ResolveSubStationId("JP13", "ABC", "JP27/ABC/ABC1,JP13/TBS/TBS1");

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task LoginRadikoAsync_短時間キャッシュ_2回目はログインAPIを再利用しない()
        {
            var loginCount = 0;
            _configMock.Setup(c => c.TryGetRadikoCredentialsAsync())
                .Returns(ValueTask.FromResult((true, "user", "pass")));

            _httpHandler.AddHandler(
                req => req.RequestUri!.ToString().Contains("https://radiko.jp/ap/member/webapi/member/login"),
                _ =>
                {
                    loginCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"radiko_session\":\"session-a\",\"paid_member\":\"1\",\"areafree\":\"1\"}")
                    };
                });

            var first = await _radikoLogic.LoginRadikoAsync();
            var second = await _radikoLogic.LoginRadikoAsync();

            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(first.Session, Is.EqualTo("session-a"));
            Assert.That(second.Session, Is.EqualTo("session-a"));
            Assert.That(loginCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AuthorizeRadikoAsync_キャッシュ破棄後はログインと認証を再取得する()
        {
            var loginCount = 0;
            var auth1Count = 0;
            var auth2Count = 0;

            _configMock.Setup(c => c.TryGetRadikoCredentialsAsync())
                .Returns(ValueTask.FromResult((true, "user", "pass")));

            _httpHandler.AddHandler(
                req => req.RequestUri!.ToString().Contains("https://radiko.jp/ap/member/webapi/member/login"),
                _ =>
                {
                    loginCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($"{{\"radiko_session\":\"session-{loginCount}\",\"paid_member\":\"1\",\"areafree\":\"1\"}}")
                    };
                });

            _httpHandler.AddHandler(
                req => req.RequestUri!.ToString().Contains("playerCommon.js"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("new RadikoJSPlayer('a','b','abcde',{")
                });

            _httpHandler.AddHandler(
                req => req.RequestUri!.ToString().Contains("auth1"),
                _ =>
                {
                    auth1Count++;
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Headers.Add("X-Radiko-AuthToken", $"token-{auth1Count}");
                    response.Headers.Add("X-Radiko-KeyLength", "5");
                    response.Headers.Add("X-Radiko-KeyOffset", "0");
                    return response;
                });

            _httpHandler.AddHandler(
                req => req.RequestUri!.ToString().Contains("auth2"),
                _ =>
                {
                    auth2Count++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($"JP13,0,{auth2Count}\n")
                    };
                });

            var first = await _radikoLogic.AuthorizeRadikoAsync();
            var second = await _radikoLogic.AuthorizeRadikoAsync();
            _radikoLogic.InvalidateAuthenticationCache();
            var third = await _radikoLogic.AuthorizeRadikoAsync();

            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(third.IsSuccess, Is.True);
            Assert.That(second.Token, Is.EqualTo(first.Token));
            Assert.That(third.Token, Is.Not.EqualTo(first.Token));
            Assert.That(loginCount, Is.EqualTo(2));
            Assert.That(auth1Count, Is.EqualTo(2));
            Assert.That(auth2Count, Is.EqualTo(2));
        }
    }
}

