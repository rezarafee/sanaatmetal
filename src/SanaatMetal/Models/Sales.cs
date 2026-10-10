using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SanaatMetal.Models;

/// <summary>خریدار (با شماره موبایل شناخته می‌شود؛ نیازی به ثبت‌نام نیست)</summary>
public class Customer : Entity
{
    [Required, MaxLength(15)] public string Mobile { get; set; } = "";
    [MaxLength(150)] public string? FullName { get; set; }
    [MaxLength(200)] public string? CompanyName { get; set; }
    [MaxLength(30)] public string? EconomicCode { get; set; }
    [MaxLength(30)] public string? NationalId { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }

    public List<Order> Orders { get; set; } = new();
    public List<Invoice> Invoices { get; set; } = new();
}

public enum OrderStatus
{
    [Display(Name = "جدید")] New = 1,
    [Display(Name = "تماس گرفته شد")] Contacted = 2,
    [Display(Name = "فاکتور شد")] Invoiced = 3,
    [Display(Name = "لغو شده")] Cancelled = 9,
}

/// <summary>سفارشی که مشتری از سبد خرید سایت ثبت می‌کند</summary>
public class Order : Entity
{
    [Required, MaxLength(20)] public string Number { get; set; } = "";
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [Required, MaxLength(15)] public string Mobile { get; set; } = "";
    [MaxLength(150)] public string? FullName { get; set; }
    [MaxLength(200)] public string? CompanyName { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.New;
    [MaxLength(1000)] public string? StaffNote { get; set; }
    [MaxLength(64)] public string? IpAddress { get; set; }
    [MaxLength(5)] public string Culture { get; set; } = "fa";
    [Column(TypeName = "decimal(18,0)")] public decimal EstimatedTotal { get; set; }
    public int? InvoiceId { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int? VariantId { get; set; }
    public ProductVariant? Variant { get; set; }
    [MaxLength(250)] public string ProductName { get; set; } = "";
    [MaxLength(200)] public string? VariantTitle { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal? UnitPrice { get; set; }
    [Column(TypeName = "decimal(5,2)")] public decimal DiscountPercent { get; set; }
}

public enum InvoiceType
{
    [Display(Name = "پیش‌فاکتور")] Proforma = 1,
    [Display(Name = "فاکتور فروش")] Sale = 2,
}

public enum InvoiceStatus
{
    [Display(Name = "پیش‌نویس")] Draft = 1,
    [Display(Name = "صادر شده")] Issued = 2,
    [Display(Name = "تسویه شده")] Paid = 3,
    [Display(Name = "باطل شده")] Cancelled = 9,
}

public enum PaymentMethod
{
    [Display(Name = "نقدی")] Cash = 1,
    [Display(Name = "چکی")] Cheque = 2,
    [Display(Name = "نقد و چک")] Mixed = 3,
    [Display(Name = "اعتباری")] Credit = 4,
}

public class Invoice : Entity
{
    public int Serial { get; set; }
    [Required, MaxLength(20)] public string Number { get; set; } = "";
    public InvoiceType Type { get; set; } = InvoiceType.Sale;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;
    public DateTime IssueDate { get; set; } = DateTime.UtcNow;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [Required, MaxLength(150)] public string CustomerName { get; set; } = "";
    [MaxLength(200)] public string? CompanyName { get; set; }
    [MaxLength(15)] public string? Mobile { get; set; }
    [MaxLength(30)] public string? EconomicCode { get; set; }
    [MaxLength(30)] public string? NationalId { get; set; }
    [MaxLength(500)] public string? Address { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    [Column(TypeName = "decimal(18,0)")] public decimal CashAmount { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal ChequeAmount { get; set; }
    public int? ChequeCount { get; set; }
    public DateTime? ChequeDueDate { get; set; }
    [MaxLength(500)] public string? ChequeDetails { get; set; }

    [Column(TypeName = "decimal(18,0)")] public decimal Subtotal { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal DiscountTotal { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal ExtraDiscount { get; set; }
    [Column(TypeName = "decimal(5,2)")] public decimal TaxPercent { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal TaxAmount { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal ShippingCost { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal GrandTotal { get; set; }
    /// <summary>جمع بهای خرید اقلام (برای گزارش سود)</summary>
    [Column(TypeName = "decimal(18,0)")] public decimal CostTotal { get; set; }

    [MaxLength(2000)] public string? Notes { get; set; }
    public int? OrderId { get; set; }

    [MaxLength(150)] public string? CreatedByName { get; set; }
    [MaxLength(150)] public string? UpdatedByName { get; set; }

    // حذف نرم: فاکتور حذف‌شده در گزارش‌ها نمی‌آید ولی برای حسابرسی باقی می‌ماند
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    [MaxLength(150)] public string? DeletedByName { get; set; }

    public List<InvoiceItem> Items { get; set; } = new();
    public List<InvoiceCheque> Cheques { get; set; } = new();

    [NotMapped] public decimal Profit => GrandTotal - TaxAmount - ShippingCost - CostTotal;
}

public class InvoiceItem
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public int? VariantId { get; set; }
    public ProductVariant? Variant { get; set; }
    [Required, MaxLength(300)] public string Description { get; set; } = "";
    [MaxLength(30)] public string? Unit { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal PurchasePrice { get; set; }
    [Column(TypeName = "decimal(5,2)")] public decimal DiscountPercent { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal DiscountAmount { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal LineTotal { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>هر چک دریافتی فاکتور با مبلغ و سررسید جداگانه</summary>
public class InvoiceCheque
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    [Column(TypeName = "decimal(18,0)")] public decimal Amount { get; set; }
    /// <summary>تاریخ سررسید (تاریخ تقویمی، بدون ساعت)</summary>
    public DateTime DueDate { get; set; }
    /// <summary>شماره چک یا شناسه صیادی</summary>
    [MaxLength(60)] public string? Number { get; set; }
    [MaxLength(100)] public string? Bank { get; set; }
    public int SortOrder { get; set; }
}
