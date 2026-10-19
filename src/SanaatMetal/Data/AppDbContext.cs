using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Models;

namespace SanaatMetal.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<PriceRecord> PriceRecords => Set<PriceRecord>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductDocument> ProductDocuments => Set<ProductDocument>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<InvoiceCheque> InvoiceCheques => Set<InvoiceCheque>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<StoredFile>().HasIndex(x => x.Path).IsUnique();

        b.Entity<Category>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Category>().HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Product>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Product>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Product>().HasOne(x => x.Category).WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Product>().HasOne(x => x.Material).WithMany(x => x.Products)
            .HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<ProductVariant>().HasOne(x => x.Product).WithMany(x => x.Variants)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PriceRecord>().HasOne(x => x.Variant).WithMany(x => x.Prices)
            .HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PriceRecord>().HasIndex(x => new { x.VariantId, x.IsCurrent });
        b.Entity<PriceRecord>().HasIndex(x => x.EffectiveFrom);

        b.Entity<Customer>().HasIndex(x => x.Mobile).IsUnique();

        b.Entity<Order>().HasIndex(x => x.Number).IsUnique();
        b.Entity<Order>().HasIndex(x => x.CreatedAt);
        b.Entity<OrderItem>().HasOne(x => x.Variant).WithMany()
            .HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Invoice>().HasIndex(x => x.Number).IsUnique();
        b.Entity<Invoice>().HasIndex(x => x.Serial).IsUnique();
        b.Entity<Invoice>().HasIndex(x => x.IssueDate);
        b.Entity<Invoice>().HasQueryFilter(x => !x.IsDeleted);
        b.Entity<Invoice>().HasOne(x => x.Customer).WithMany(x => x.Invoices)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<InvoiceItem>().HasQueryFilter(x => !x.Invoice!.IsDeleted);
        b.Entity<InvoiceCheque>().HasQueryFilter(x => !x.Invoice!.IsDeleted);
        b.Entity<InvoiceCheque>().HasIndex(x => x.DueDate);
        b.Entity<InvoiceItem>().HasOne(x => x.Variant).WithMany()
            .HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<ContactMessage>().HasIndex(x => x.CreatedAt);
        b.Entity<RolePermission>().HasIndex(x => new { x.RoleName, x.Permission }).IsUnique();
        b.Entity<AuditLog>().HasIndex(x => x.At);

        b.Entity<SiteSettings>().Property(x => x.DefaultTaxPercent).HasPrecision(5, 2);

        // Sqlite فقط برای اجرای آزمایشی محلی است و decimal را در مرتب‌سازی/تجمیع پشتیبانی نمی‌کند
        if (Database.IsSqlite())
        {
            foreach (var prop in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
                prop.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal, double>(v => (double)v, v => (decimal)v));
        }
    }
}
