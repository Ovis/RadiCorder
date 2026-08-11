using System.Runtime.InteropServices;
using NUnit.Framework.Legacy;
using RadiCorder.Logics.Extensions;

namespace RadiCorder.Logics.Tests.ExtensionsTest
{
    [TestFixture]
    public class FolderPathExtensionsTests
    {
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("invalid*fileName")]
        [TestCase("invalid?fileName")]
        [TestCase("invalid<fileName")]
        [TestCase("invalid>fileName")]
        [TestCase("invalid|fileName")]
        [TestCase("invalid:fileName")]
        [TestCase("invalid\"fileName")]
        [TestCase("invalid\\fileName")]
        [TestCase("invalid/fileName")]
        [TestCase("\\..\\fileName")]
        public void IsValidFileName_InvalidFileName_ReturnsFalse(string fileName)
        {
            var result = fileName.IsValidFileName();

            ClassicAssert.IsFalse(result);
        }


        [TestCase("validFileName")]
        [TestCase("valid_file_name")]
        [TestCase("valid-file-name")]
        [TestCase("valid.file.name")]
        [TestCase("validFileName1234567890")]
        [TestCase("validFileName1234567890abcdefghijklmnopqrstuvwxyz")]
        [TestCase("validFileName1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ")]
        [TestCase("validFileName1234567890!#$%&'()=~^`{}+@_-.")]
        public void IsValidFileName_ValidFileName_ReturnsTrue(string fileName)
        {
            var result = fileName.IsValidFileName();

            ClassicAssert.IsTrue(result);
        }


        [TestCase("")]
        [TestCase(" ")]
        [TestCase(".\\InvalidPath")]
        public void IsValidAbsolutePath_InvalidPath_ReturnsFalseForWindowsPlatform(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var result = path.IsValidAbsolutePath();

                ClassicAssert.IsFalse(result);
            }
            else
            {
                Assert.Pass();
            }
        }


        [TestCase("")]
        [TestCase(" ")]
        public void IsValidAbsolutePath_InvalidPath_ReturnsFalseForLinuxPlatform(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var result = path.IsValidAbsolutePath();

                ClassicAssert.IsFalse(result);
            }
            else
            {
                Assert.Pass();
            }
        }


        [TestCase("C:\\valid\\path")]
        public void IsValidAbsolutePath_ValidPath_ReturnsTrueForWindowsPlatform(string path)
        {
            // 実行環境がWindowsの場合のみテスト
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var result = path.IsValidAbsolutePath();

                ClassicAssert.IsTrue(result);
            }
            else
            {
                Assert.Pass();
            }
        }


        /// <summary>
        /// Windows環境におけるネットワークドライブパスが指定された場合の絶対パス判定動作確認
        /// </summary>
        [Test]
        public void IsValidAbsolutePath_ValidPath_ReturnsTrueForWindowsPlatformNetworkPath()
        {
            // 実行環境がWindowsの場合のみテスト
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var localPath = Path.Combine(Environment.CurrentDirectory, "NetworkDriveSim");

                if (!Directory.Exists(localPath))
                    Directory.CreateDirectory(localPath);

                var networkDrivePath = $"\\\\localhost\\{localPath.Replace(':', '$')}";

                var result = networkDrivePath.IsValidAbsolutePath();

                ClassicAssert.IsTrue(result);
            }
            else
            {
                Assert.Pass();
            }
        }


        [TestCase("/valid/path")]
        public void IsValidAbsolutePath_ValidPath_ReturnsTrueForLinuxPlatform(string path)
        {
            // 実行環境がLinuxの場合のみテスト
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var result = path.IsValidAbsolutePath();

                ClassicAssert.IsTrue(result);
            }
            else
            {
                Assert.Pass();
            }
        }


        [TestCase("")]
        [TestCase(" ")]
        public void IsValidRelativePath_InvalidPath_ReturnsFalse(string path)
        {
            var result = path.IsValidRelativePath();

            ClassicAssert.IsFalse(result);
        }


        [TestCase("valid\\relative\\path")]
        public void IsValidRelativePath_ValidPath_ReturnsTrueForWindowsPlatform(string path)
        {
            // 実行環境がWindowsの場合のみテスト
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var result = path.IsValidRelativePath();

                ClassicAssert.IsTrue(result);
            }
            else
            {
                Assert.Pass();
            }
        }


        [TestCase("valid/relative/path")]
        public void IsValidRelativePath_ValidPath_ReturnsTrueForLinuxPlatform(string path)
        {
            // 実行環境がLinuxの場合のみテスト
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var result = path.IsValidRelativePath();

                ClassicAssert.IsTrue(result);
            }
            else
            {
                Assert.Pass();
            }
        }


        [TestCase("C:\\base\\path", "valid\\relative\\path")]
        public void TryCombinePaths_ValidPaths_ReturnsTrueForWindowsPlatform(string basePath, string relativePath)
        {
            // 実行環境がWindowsの場合のみテスト
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var result = basePath.TryCombinePaths(relativePath, out var combinedPath);

                ClassicAssert.IsTrue(result);
                ClassicAssert.IsNotNull(combinedPath);
            }
            else
            {
                Assert.Pass();
            }
        }


        [TestCase("/base/path", "valid/relative/path")]
        public void TryCombinePaths_ValidPaths_ReturnsTrueForLinuxPlatform(string basePath, string relativePath)
        {
            // 実行環境がLinuxの場合のみテスト
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var result = basePath.TryCombinePaths(relativePath, out var combinedPath);

                ClassicAssert.IsTrue(result);
                ClassicAssert.IsNotNull(combinedPath);
            }
            else
            {
                Assert.Pass();
            }
        }




        [TestCase("C:\\base\\path", "C:\\invalid\\relative\\path")]
        public void TryCombinePaths_InvalidPaths_ReturnsFalseForWindowsPlatform(string basePath, string relativePath)
        {
            // 実行環境がWindowsの場合のみテスト
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var result = basePath.TryCombinePaths(relativePath, out var combinedPath);

                ClassicAssert.IsFalse(result);
                ClassicAssert.IsNotNull(combinedPath);
            }
            else
            {
                Assert.Pass();
            }
        }


        [TestCase("/base/path", "../relative/path")]
        public void TryCombinePaths_InvalidPaths_ReturnsFalseForLinuxPlatform(string basePath, string relativePath)
        {
            // 実行環境がLinuxの場合のみテスト
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var result = basePath.TryCombinePaths(relativePath, out var combinedPath);

                ClassicAssert.IsFalse(result);
                ClassicAssert.IsNotNull(combinedPath);
            }
            else
            {
                Assert.Pass();
            }
        }

        [Test]
        public void TryCombinePaths_ルート文字列プレフィックス一致のみの別パスは拒否する()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"rk-base-{Guid.NewGuid():N}");
            var siblingPath = tempRoot + "_sibling";
            Directory.CreateDirectory(tempRoot);

            try
            {
                var relativePath = $"..{Path.DirectorySeparatorChar}{Path.GetFileName(siblingPath)}{Path.DirectorySeparatorChar}file.m4a";

                var result = tempRoot.TryCombinePaths(relativePath, out var combinedPath);

                ClassicAssert.IsFalse(result);
                Assert.That(combinedPath, Is.EqualTo(string.Empty));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }
    }
}
