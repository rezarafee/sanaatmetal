using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SanaatMetal.Models;

public abstract class Entity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>دسته‌بندی محصولات (فلنج، شیرآلات، لوله، اتصالات ...)</summary>
public class Category : Entity
{
    [Required, MaxLength(150)] public string NameFa { get; set; } = "";
    [MaxLength(150)] public string? NameEn { get; set; }
    [Required, MaxLength(160)] public string Slug { get; set; } = "";
    [MaxLength(1000)] public string? DescriptionFa { get; set; }
    [MaxLength(1000)] public string? DescriptionEn { get; set; }
    [MaxLength(400)] public string? ImagePath { get; set; }
    [MaxLength(60)] public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = new();
    public List<Product> Products { get; set; } = new();
}

/// <summary>متریال / جنس (A105، SS316، API 5L ...)</summary>
public class Material : Entity
{
    [Required, MaxLength(150)] public string NameFa { get; set; } = "";
    [MaxLength(150)] public string? NameEn { get; set; }
    [MaxLength(60)] public string? Code { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public List<Product> Products { get; set; } = new();
}

public class Product : Entity
{
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? MaterialId { get; set; }
    public Material? Material { get; set; }

    [Required, MaxLength(60)] public string Code { get; set; } = "";
    [Required, MaxLength(200)] public string Slug { get; set; } = "";
    [Required, MaxLength(250)] public string NameFa { get; set; } = "";
    [MaxLength(250)] public string? NameEn { get; set; }
    [MaxLength(600)] public string? SummaryFa { get; set; }
    [MaxLength(600)] public string? SummaryEn { get; set; }
    public string? DescriptionFa { get; set; }
    public string? DescriptionEn { get; set; }
    /// <summary>استاندارد ساخت، مثلا ASME B16.5</summary>
    [MaxLength(150)] public string? Standard { get; set; }
    [MaxLength(100)] public string? Brand { get; set; }
    [MaxLength(100)] public string? Origin { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    /// <summary>اگر فعال باشد به‌جای قیمت «تماس بگیرید» نمایش داده می‌شود</summary>
    public bool CallForPrice { get; set; }
    public int SortOrder { get; set; }
    public int ViewCount { get; set; }

    public List<ProductVariant> Variants { get; set; } = new();
    public List<ProductImage> Images { get; set; } = new();
    public List<ProductDocument> Documents { get; set; } = new();
}

/// <summary>هر سایز / کلاس / اسکجول یک محصول؛ قیمت روی همین سطح است</summary>
public class ProductVariant : Entity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Required, MaxLength(60)] public string Size { get; set; } = "";
    [MaxLength(60)] public string? PressureClass { get; set; }
    [MaxLength(60)] public string? Schedule { get; set; }
    [MaxLength(60)] public string? Sku { get; set; }
    [MaxLength(30)] public string Unit { get; set; } = "عدد";
    [MaxLength(300)] public string? ExtraSpecs { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Stock { get; set; }
    public bool IsActive { get; set; } = true;
    public bool CallForPrice { get; set; }
    public int SortOrder { get; set; }

    // آخرین قیمت معتبر (برای نمایش سریع؛ تاریخچه در PriceRecord است)
    [Column(TypeName = "decimal(18,0)")] public decimal? SalePrice { get; set; }
    [Column(TypeName = "decimal(5,2)")] public decimal DiscountPercent { get; set; }

    public List<PriceRecord> Prices { get; set; } = new();

    [NotMapped]
    public decimal? FinalPrice => SalePrice is null ? null : Math.Round(SalePrice.Value * (100 - DiscountPercent) / 100m, 0);

    [NotMapped]
    public string Title => string.Join(" - ", new[] { Size, PressureClass, Schedule }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>جدول قیمت: خرید، فروش، نقدی، چکی، تخفیف و ... با تاریخچه کامل</summary>
public class PriceRecord : Entity
{
    public int VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    [Column(TypeName = "decimal(18,0)")] public decimal PurchasePrice { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal SalePrice { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal? CashPrice { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal? ChequePrice { get; set; }
    public int? ChequeTermDays { get; set; }
    [Column(TypeName = "decimal(5,2)")] public decimal DiscountPercent { get; set; }
    [MaxLength(150)] public string? Supplier { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public bool IsCurrent { get; set; } = true;
    [MaxLength(450)] public string? CreatedById { get; set; }
    [MaxLength(150)] public string? CreatedByName { get; set; }
}

public class ProductImage : Entity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    [Required, MaxLength(400)] public string FilePath { get; set; } = "";
    [MaxLength(200)] public string? AltFa { get; set; }
    [MaxLength(200)] public string? AltEn { get; set; }
    public int SortOrder { get; set; }
    public bool IsMain { get; set; }
    /// <summary>نمایش در اسلایدر صفحه اصلی</summary>
    public bool ShowInHomeSlider { get; set; }
}

public enum DocumentType
{
    [Display(Name = "گواهی‌نامه")] Certificate = 1,
    [Display(Name = "دیتاشیت")] Datasheet = 2,
    [Display(Name = "کاتالوگ")] Catalog = 3,
    [Display(Name = "سایر")] Other = 9,
}

/// <summary>گواهی‌ها و دیتاشیت‌ها؛ نسخه‌های قدیمی حذف نمی‌شوند و فقط غیرفعال می‌شوند</summary>
public class ProductDocument : Entity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public DocumentType Type { get; set; } = DocumentType.Certificate;
    [Required, MaxLength(250)] public string TitleFa { get; set; } = "";
    [MaxLength(250)] public string? TitleEn { get; set; }
    [Required, MaxLength(400)] public string FilePath { get; set; } = "";
    [MaxLength(260)] public string? OriginalFileName { get; set; }
    public long FileSize { get; set; }
    [MaxLength(50)] public string? Version { get; set; }
    [MaxLength(150)] public string? Issuer { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    [NotMapped] public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value.Date < DateTime.UtcNow.Date;
}
