using System.ComponentModel.DataAnnotations;
using SanaatMetal.Models;

namespace SanaatMetal.ViewModels;

public class ProductCardVm
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Summary { get; set; }
    public string? Category { get; set; }
    public string? Material { get; set; }
    public string? Standard { get; set; }
    public string Image { get; set; } = "/img/placeholder.svg";
    public bool CallForPrice { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MinFinalPrice { get; set; }
    public decimal MaxDiscount { get; set; }
    public int VariantCount { get; set; }
    public int? SingleVariantId { get; set; }
}

public class HeroSlideVm
{
    public string Image { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? Url { get; set; }
}

public class HomeVm
{
    public List<HeroSlideVm> Slides { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<ProductCardVm> Featured { get; set; } = new();
    public List<Client> Clients { get; set; } = new();
    public int ProductCount { get; set; }
    public int DocumentCount { get; set; }
    public ContactFormVm Contact { get; set; } = new();
}

public class ProductListVm
{
    public List<ProductCardVm> Items { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Material> Materials { get; set; } = new();
    public Dictionary<int, int> CategoryCounts { get; set; } = new();
    public string? Q { get; set; }
    public string? Cat { get; set; }
    public int? Mat { get; set; }
    public string Sort { get; set; } = "new";
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int Total { get; set; }
    public Category? CurrentCategory { get; set; }
}

public class ProductDetailVm
{
    public Product Product { get; set; } = null!;
    public List<ProductDocument> Certificates { get; set; } = new();
    public List<ProductDocument> Datasheets { get; set; } = new();
    public List<ProductCardVm> Related { get; set; } = new();
}

public class ContactFormVm
{
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [Required, EmailAddress, MaxLength(200)] public string Email { get; set; } = "";
    [MaxLength(20)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Company { get; set; }
    [MaxLength(200)] public string? Subject { get; set; }
    [Required, MaxLength(4000)] public string Body { get; set; } = "";
    /// <summary>فیلد مخفی ضد ربات</summary>
    public string? Website { get; set; }
}

public class CheckoutVm
{
    [Required, MaxLength(20)] public string Mobile { get; set; } = "";
    [MaxLength(150)] public string? FullName { get; set; }
    [MaxLength(200)] public string? CompanyName { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    public string? ItemsJson { get; set; }
    public string? Website { get; set; }
}

public record CartLine(int v, decimal q);
