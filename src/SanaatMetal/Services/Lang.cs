using System.Globalization;

namespace SanaatMetal.Services;

/// <summary>زبان صفحات عمومی (فارسی / انگلیسی). پنل ادمین همیشه فارسی است.</summary>
public class Lang(SettingsService settings)
{
    public static readonly string[] Supported = ["fa", "en"];

    public string Code => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "fa";
    public bool IsFa => Code == "fa";
    public bool IsEn => !IsFa;
    public string Dir => IsFa ? "rtl" : "ltr";
    public string Other => IsFa ? "en" : "fa";

    private IReadOnlyDictionary<string, (string? Fa, string? En)>? _overrides;
    private IReadOnlyDictionary<string, (string? Fa, string? En)> Overrides => _overrides ??= settings.Get().Texts();

    /// <summary>متن رابط کاربری؛ اگر مدیر در «متن‌های سایت» متن دیگری گذاشته باشد همان برگردانده می‌شود</summary>
    public string this[string key]
    {
        get
        {
            if (Overrides.TryGetValue(key, out var o))
            {
                var v = IsFa ? o.Fa : o.En;
                if (!string.IsNullOrWhiteSpace(v)) return v!;
            }
            return Texts.TryGetValue(key, out var t) ? (IsFa ? t.Fa : t.En) : key;
        }
    }

    /// <summary>متن پیش‌فرض فارسی/انگلیسی همه کلیدها (برای صفحه «متن‌های سایت» در پنل)</summary>
    public static IReadOnlyDictionary<string, (string Fa, string En)> Defaults => Texts;

    /// <summary>کلیدهایی که در صفحه «متن‌های سایت» قابل ویرایش هستند، به ترتیب و با گروه</summary>
    public static readonly (string Group, string[] Keys)[] EditableGroups =
    [
        ("منو و عمومی", ["Home", "Products", "AllProducts", "Categories", "Downloads", "About", "Contact", "Cart", "SearchPlaceholder", "QuickLinks", "Rights"]),
        ("بنر و صفحه اصلی", ["HeroPopular", "HeroQ1", "HeroQ2", "HeroQ3", "ViewProducts", "RequestQuote", "FeaturedProducts", "OurClients", "OurClientsSub",
            "StatProducts", "StatClients", "StatCerts", "StatYears"]),
        ("چرا ما؟", ["WhyUs", "Why1", "Why1d", "Why2", "Why2d", "Why3", "Why3d", "Why4", "Why4d"]),
        ("محصول و مشخصات", ["Size", "Class", "Schedule", "Unit", "Material", "Standard", "Brand", "Origin", "Code", "Specifications", "SelectSize",
            "AllMaterials", "CallForPrice", "AddToCart", "Certificates", "Datasheets", "Documents", "NoDocuments", "RelatedProducts", "ProductsMetaDesc", "DownloadsMetaDesc"]),
        ("سبد خرید و تماس", ["CartNote", "CheckoutHint", "OrderSuccessText2", "ContactTitle", "ContactSub", "ContactThanks", "Office1", "Office2"]),
    ];

    /// <summary>انتخاب محتوای دو زبانه؛ اگر انگلیسی خالی بود فارسی برمی‌گردد</summary>
    public string Pick(string? fa, string? en) => IsEn && !string.IsNullOrWhiteSpace(en) ? en! : fa ?? en ?? "";

    public string Num(decimal? v) => Fmt.Number(v, IsFa);
    public string Num(int v) => Fmt.Number(v, IsFa);
    public string Date(DateTime? d) => IsFa ? Fmt.Jalali(d) : Fmt.Gregorian(d);

    private static readonly Dictionary<string, (string Fa, string En)> Texts = new()
    {
        ["Home"] = ("خانه", "Home"),
        ["Products"] = ("محصولات", "Products"),
        ["AllProducts"] = ("همه محصولات", "All products"),
        ["Categories"] = ("دسته‌بندی‌ها", "Categories"),
        ["Downloads"] = ("کاتالوگ و مستندات", "Catalogs & documents"),
        ["About"] = ("درباره ما", "About us"),
        ["Contact"] = ("تماس با ما", "Contact us"),
        ["Cart"] = ("سبد خرید", "Cart"),
        ["Search"] = ("جستجو", "Search"),
        ["SearchPlaceholder"] = ("جستجوی محصول، سایز یا کد...", "Search product, size or code..."),
        ["CallShort"] = ("تماس", "Call"),
        ["HeroPopular"] = ("پرجستجو:", "Popular:"),
        ["HeroQ1"] = ("", ""),
        ["HeroQ2"] = ("", ""),
        ["HeroQ3"] = ("", ""),
        ["ViewProducts"] = ("مشاهده محصولات", "Browse products"),
        ["RequestQuote"] = ("درخواست استعلام", "Request a quote"),
        ["FeaturedProducts"] = ("محصولات ویژه", "Featured products"),
        ["OurClients"] = ("مشتریان ما", "Our clients"),
        ["OurClientsSub"] = ("افتخار همکاری با مجموعه‌های معتبر کشور", "Trusted by leading businesses"),
        ["WhyUs"] = ("چرا ما؟", "Why choose us?"),
        ["Why1"] = ("کالای اصل و استاندارد", "Genuine, standard products"),
        ["Why1d"] = ("همه محصولات اصل، با ضمانت و مستندات معتبر عرضه می‌شوند.", "Every product is genuine and comes with warranty and documentation."),
        ["Why2"] = ("تنوع بالای محصولات", "Wide product range"),
        ["Why2d"] = ("مدل‌ها، برندها و سایزهای مختلف را یک‌جا پیدا کنید.", "Find many models, brands and sizes in one place."),
        ["Why3"] = ("ارسال سریع", "Fast delivery"),
        ["Why3d"] = ("تأمین و ارسال سریع سفارش‌ها به سراسر کشور.", "Quick supply and nationwide shipping."),
        ["Why4"] = ("مشاوره تخصصی", "Expert advice"),
        ["Why4d"] = ("کارشناسان ما در انتخاب کالای مناسب کنار شما هستند.", "Our experts help you pick the right item."),
        ["CallForPrice"] = ("تماس بگیرید", "Call for price"),
        ["Price"] = ("قیمت", "Price"),
        ["AddToCart"] = ("افزودن به سبد", "Add to cart"),
        ["Added"] = ("به سبد اضافه شد", "Added to cart"),
        ["Details"] = ("جزئیات", "Details"),
        ["Size"] = ("سایز", "Size"),
        ["Class"] = ("کلاس", "Class"),
        ["Schedule"] = ("اسکجول", "Schedule"),
        ["Unit"] = ("واحد", "Unit"),
        ["Material"] = ("متریال", "Material"),
        ["Standard"] = ("استاندارد", "Standard"),
        ["Brand"] = ("برند", "Brand"),
        ["Origin"] = ("کشور سازنده", "Origin"),
        ["Code"] = ("کد کالا", "Code"),
        ["Category"] = ("دسته‌بندی", "Category"),
        ["Description"] = ("توضیحات", "Description"),
        ["Specifications"] = ("مشخصات و قیمت سایزها", "Sizes, specs & prices"),
        ["Certificates"] = ("گواهی‌نامه‌ها", "Certificates"),
        ["Datasheets"] = ("دیتاشیت‌ها", "Datasheets"),
        ["Documents"] = ("مستندات", "Documents"),
        ["Download"] = ("دانلود", "Download"),
        ["Version"] = ("نسخه", "Version"),
        ["ValidUntil"] = ("معتبر تا", "Valid until"),
        ["Expired"] = ("منقضی", "Expired"),
        ["Issuer"] = ("صادرکننده", "Issuer"),
        ["NoDocuments"] = ("مستندی برای این محصول ثبت نشده است.", "No documents for this product yet."),
        ["RelatedProducts"] = ("محصولات مرتبط", "Related products"),
        ["Filter"] = ("فیلتر", "Filter"),
        ["AllCategories"] = ("همه دسته‌ها", "All categories"),
        ["AllMaterials"] = ("همه متریال‌ها", "All materials"),
        ["Sort"] = ("مرتب‌سازی", "Sort"),
        ["SortNewest"] = ("جدیدترین", "Newest"),
        ["SortName"] = ("نام", "Name"),
        ["SortPriceAsc"] = ("ارزان‌ترین", "Price: low to high"),
        ["SortPriceDesc"] = ("گران‌ترین", "Price: high to low"),
        ["NoProducts"] = ("محصولی یافت نشد.", "No products found."),
        ["From"] = ("از", "From"),
        ["Discount"] = ("تخفیف", "Off"),
        ["Quantity"] = ("تعداد", "Qty"),
        ["Remove"] = ("حذف", "Remove"),
        ["CartEmpty"] = ("سبد خرید شما خالی است.", "Your cart is empty."),
        ["ContinueShopping"] = ("ادامه خرید", "Continue shopping"),
        ["CartTotal"] = ("جمع تقریبی", "Estimated total"),
        ["CartNote"] = ("قیمت نهایی و هزینه ارسال پس از تماس کارشناس فروش در پیش‌فاکتور اعلام می‌شود.", "Final price and shipping will be confirmed by our sales team in the proforma invoice."),
        ["Checkout"] = ("ثبت سفارش", "Place order"),
        ["CheckoutTitle"] = ("اطلاعات تماس", "Contact details"),
        ["CheckoutHint"] = ("فقط شماره موبایل کافی است؛ کارشناسان ما با شما تماس می‌گیرند.", "Only your mobile number is needed; our team will call you."),
        ["Mobile"] = ("شماره موبایل", "Mobile number"),
        ["FullName"] = ("نام و نام خانوادگی", "Full name"),
        ["Company"] = ("نام شرکت", "Company"),
        ["Optional"] = ("اختیاری", "optional"),
        ["OrderNote"] = ("توضیحات سفارش", "Order notes"),
        ["OrderSuccess"] = ("سفارش شما ثبت شد", "Your order has been placed"),
        ["OrderSuccessText"] = ("شماره پیگیری سفارش شما:", "Your tracking number:"),
        ["OrderSuccessText2"] = ("کارشناسان فروش به‌زودی با شما تماس می‌گیرند.", "Our sales team will contact you shortly."),
        ["InvalidMobile"] = ("شماره موبایل معتبر نیست (مثال: ۰۹۱۲۳۴۵۶۷۸۹).", "Invalid mobile number (e.g. 09123456789)."),
        ["Items"] = ("قلم", "items"),
        ["ContactTitle"] = ("درخواست خود را برای ما بفرستید", "Send us your request"),
        ["ContactSub"] = ("استعلام قیمت، سفارش پروژه‌ای یا هر سؤال فنی؛ در کوتاه‌ترین زمان پاسخ می‌دهیم.", "Price inquiries, project orders or technical questions; we reply quickly."),
        ["Name"] = ("نام", "Name"),
        ["Email"] = ("ایمیل", "Email"),
        ["Phone"] = ("تلفن", "Phone"),
        ["Subject"] = ("موضوع", "Subject"),
        ["Message"] = ("متن درخواست", "Your request"),
        ["Send"] = ("ارسال درخواست", "Send request"),
        ["ContactThanks"] = ("درخواست شما با موفقیت ثبت شد. به‌زودی پاسخ می‌دهیم.", "Thank you! Your request was received; we will reply soon."),
        ["ContactInvalid"] = ("لطفاً نام، ایمیل معتبر و متن درخواست را وارد کنید.", "Please enter your name, a valid email and your request."),
        ["TooMany"] = ("درخواست‌های زیادی ارسال شده است؛ لطفاً کمی بعد دوباره تلاش کنید.", "Too many requests; please try again shortly."),
        ["Address"] = ("نشانی", "Address"),
        ["Office1"] = ("دفتر مرکزی", "Head office"),
        ["Office2"] = ("دفتر دوم", "Second office"),
        ["WorkingHours"] = ("ساعات کاری", "Working hours"),
        ["QuickLinks"] = ("دسترسی سریع", "Quick links"),
        ["FollowUs"] = ("ما را دنبال کنید", "Follow us"),
        ["Rights"] = ("تمامی حقوق محفوظ است.", "All rights reserved."),
        ["ThemeToggle"] = ("تغییر تم", "Toggle theme"),
        ["Language"] = ("English", "فارسی"),
        ["Page"] = ("صفحه", "Page"),
        ["Prev"] = ("قبلی", "Previous"),
        ["Next"] = ("بعدی", "Next"),
        ["StatProducts"] = ("محصول", "Products"),
        ["StatClients"] = ("مشتری", "Clients"),
        ["StatCerts"] = ("گواهی و دیتاشیت", "Certificates"),
        ["StatYears"] = ("سال تجربه", "Years of experience"),
        ["NotFound"] = ("صفحه مورد نظر پیدا نشد.", "Page not found."),
        ["BackHome"] = ("بازگشت به خانه", "Back to home"),
        ["Error"] = ("خطایی رخ داد. لطفاً دوباره تلاش کنید.", "Something went wrong. Please try again."),
        ["Type_Certificate"] = ("گواهی‌نامه", "Certificate"),
        ["Type_Datasheet"] = ("دیتاشیت", "Datasheet"),
        ["Type_Catalog"] = ("کاتالوگ", "Catalog"),
        ["Type_Other"] = ("سایر", "Other"),
        ["Product"] = ("محصول", "Product"),
        ["Currency"] = ("ریال", "IRR"),
        ["Close"] = ("بستن", "Close"),
        ["ViewCart"] = ("مشاهده سبد خرید", "View cart"),
        ["SelectSize"] = ("انتخاب سایز", "Select size"),
        ["ProductsMetaDesc"] = ("لیست محصولات با مشخصات، قیمت و مستندات.", "Product list with specifications, prices and documents."),
        ["DownloadsMetaDesc"] = ("دانلود کاتالوگ‌ها، گواهی‌نامه‌ها و دیتاشیت‌های محصولات.", "Download product catalogs, certificates and datasheets."),
    };
}
