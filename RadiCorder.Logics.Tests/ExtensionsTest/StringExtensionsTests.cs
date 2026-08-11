using RadiCorder.Logics.Extensions;

namespace RadiCorder.Logics.Tests.ExtensionsTest
{
    [TestFixture]
    public class StringExtensionsTests
    {
        [Test]
        public void ToSafeName_ファイル名に利用できない文字列の置き換えテスト()
        {
            var filePath = @"\/My:File*Name?""<|>.txt";

            var safeName = filePath.ToSafeName();

            Assert.That(safeName, Is.EqualTo(@"￥／My：File＊Name？”＜｜＞.txt"));
        }


        [Test]
        public void To半角英数字_全角英数字To半角英数字()
        {
            var input = "０１２３４５６７８９ＡＢＣＤＥＦＧＨＩＪＫＬＭＮＯＰＱＲＳＴＵＶＷＸＹＺａｂｃｄｅｆｇｈｉｊｋｌｍｎｏｐｑｒｓｔｕｖｗｘｙｚ";

            var converted = input.To半角英数字();

            Assert.That(converted, Is.EqualTo("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"));
        }


        [Test]
        public void ContainsAny_指定された文字列が含まれるかどうかの判定テスト()
        {
            var source = "Hello, World!";
            char[] chars = ['!', '?', ','];

            var containsAny = source.ContainsAny(chars);

            Assert.That(containsAny, Is.True);
        }


        [Test]
        public void ParseKeywords_キーワードの切り出しテスト()
        {
            // Arrange
            var input = "apple \"water melon\" cherry orange";

            // Act
            var keywords = input.ParseKeywords();

            // Assert
            Assert.That(keywords.Count, Is.EqualTo(4));
            Assert.That(keywords.Contains("apple"), Is.True);
            Assert.That(keywords.Contains("water melon"), Is.True);
            Assert.That(keywords.Contains("cherry"), Is.True);
            Assert.That(keywords.Contains("orange"), Is.True);
        }
    }
}
