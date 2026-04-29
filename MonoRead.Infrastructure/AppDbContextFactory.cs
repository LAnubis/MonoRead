using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Infrastructure
{
    // 这个类只在执行 dotnet ef 命令时被调用，App 运行时不会用到它
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

            // 这里的路径仅供生成迁移脚本和测试建表使用
            optionsBuilder.UseSqlite("Data Source=monoread_dev.db3");

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
