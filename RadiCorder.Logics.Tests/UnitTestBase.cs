using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests
{
    public abstract class UnitTestBase
    {
        protected RadioDbContext DbContext = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var options = new DbContextOptionsBuilder<RadioDbContext>()
                .UseSqlite("Data Source=radio-unittest.db")
                .Options;
            DbContext = new RadioDbContext(options);

            DbContext.Database.EnsureDeleted();
            DbContext.Database.EnsureCreated();

            // 初期データ登録
            SeedData(DbContext);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // データベースのクリーンアップ
            DbContext.Database.EnsureDeleted();
            DbContext.Dispose();
        }


        private void SeedData(RadioDbContext context)
        {
            // データの初期化
            context.SaveChanges();
        }
    }
}
