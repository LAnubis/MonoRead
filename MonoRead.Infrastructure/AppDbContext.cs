using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MonoRead.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.Infrastructure
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Folder> Folders => Set<Folder>();
        public DbSet<Book> Books => Set<Book>();
        public DbSet<BookChapter> BookChapters => Set<BookChapter>();
        public DbSet<HighlightNote> HighlightNotes => Set<HighlightNote>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. 配置所有继承自 BaseEntity 的实体
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                {
                    // 全局软删除过滤器：查询时自动排除 IsDeleted 为 true 的记录
                    modelBuilder.Entity(entityType.ClrType).HasQueryFilter(GenerateIsDeletedFilter(entityType.ClrType));
                }
            }

            // 2. 显式配置外键级联删除策略为 Restrict
            // 防止删除书籍时导致笔记被物理级联删除
            foreach (var relationship in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            {
                relationship.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }

        private static System.Linq.Expressions.LambdaExpression GenerateIsDeletedFilter(Type type)
        {
            var parameter = System.Linq.Expressions.Expression.Parameter(type, "e");
            var property = System.Linq.Expressions.Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
            var comparison = System.Linq.Expressions.Expression.Equal(property, System.Linq.Expressions.Expression.Constant(false));
            return System.Linq.Expressions.Expression.Lambda(comparison, parameter);
        }

        // 3. 自动审计：在保存时自动更新时间戳
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var entries = ChangeTracker.Entries().Where(e => e.Entity is BaseEntity && (e.State == EntityState.Added || e.State == EntityState.Modified));

            foreach (var entry in entries)
            {
                var entity = (BaseEntity)entry.Entity;
                entity.UpdatedAt = DateTime.UtcNow;

                if (entry.State == EntityState.Added)
                {
                    entity.CreatedAt = DateTime.UtcNow;
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
