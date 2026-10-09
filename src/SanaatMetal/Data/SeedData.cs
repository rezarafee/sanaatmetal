using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SanaatMetal.Models;
using SanaatMetal.Security;

namespace SanaatMetal.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider sp, string provider)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var config = sp.GetRequiredService<IConfiguration>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");

        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            await db.Database.EnsureCreatedAsync();
        else
            await db.Database.MigrateAsync();

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var r in Roles.All)
            if (!await roleManager.RoleExistsAsync(r))
                await roleManager.CreateAsync(new IdentityRole(r));

        if (!await db.RolePermissions.AnyAsync())
        {
            foreach (var (role, perms) in Permissions.Defaults)
                foreach (var p in perms)
                    db.RolePermissions.Add(new RolePermission { RoleName = role, Permission = p });
            await db.SaveChangesAsync();
        }

        var users = sp.GetRequiredService<UserManager<AppUser>>();
        if (!await users.Users.AnyAsync())
        {
            var email = config["Seed:AdminEmail"];
            var password = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                log.LogWarning("No users exist. Set Seed:AdminEmail and Seed:AdminPassword to create the first super admin.");
            }
            else
            {
                var admin = new AppUser
                {
                    UserName = config["Seed:AdminUserName"] ?? "superadmin",
                    Email = email,
                    EmailConfirmed = true,
                    FullName = "مدیر کل سیستم",
                };
                var res = await users.CreateAsync(admin, password);
                if (res.Succeeded) await users.AddToRoleAsync(admin, Roles.SuperAdmin);
                else log.LogError("Super admin creation failed: {Errors}", string.Join("; ", res.Errors.Select(e => e.Description)));
            }
        }

        if (!await db.SiteSettings.AnyAsync())
        {
            db.SiteSettings.Add(new SiteSettings
            {
                CompanyNameFa = "صنعت متال",
                CompanyNameEn = "Sanaat Metal",
                SloganFa = "تأمین‌کننده لوله، فلنج، شیرآلات و اتصالات صنعتی",
                SloganEn = "Industrial pipes, flanges, valves & fittings supplier",
                HeroTitleFa = "تأمین‌کننده تخصصی لوله، فلنج و شیرآلات صنعتی",
                HeroTitleEn = "Specialist supplier of industrial pipes, flanges & valves",
                HeroSubtitleFa = "کالای استاندارد با گواهی معتبر برای پالایشگاه‌ها، پتروشیمی‌ها و صنایع بزرگ",
                HeroSubtitleEn = "Certified, standard materials for refineries, petrochemical plants and heavy industry",
                AboutFa = "ما با سال‌ها تجربه در تأمین کالای صنعتی، انواع لوله، فلنج، شیرآلات و اتصالات را در سایزها و متریال‌های مختلف همراه با گواهی‌نامه‌ها و دیتاشیت‌های معتبر عرضه می‌کنیم.",
                AboutEn = "With years of experience in industrial supply, we provide pipes, flanges, valves and fittings in many sizes and materials, with valid certificates and datasheets.",
                Phone = "021-00000000",
                Email = "info@sanaatmetal.ir",
                AddressFa = "تهران، ...",
                AddressEn = "Tehran, Iran",
                WorkingHoursFa = "شنبه تا چهارشنبه ۸ تا ۱۷",
                WorkingHoursEn = "Sat–Wed, 8:00–17:00",
                DefaultTaxPercent = 10,
                InvoiceFooter = "اعتبار پیش‌فاکتور ۳ روز کاری است.",
            });
            await db.SaveChangesAsync();
        }

        if (config.GetValue<bool>("Seed:DemoData") && !await db.Products.AnyAsync())
            await SeedDemoAsync(db);
    }

    private static async Task SeedDemoAsync(AppDbContext db)
    {
        var flange = new Category { NameFa = "فلنج", NameEn = "Flanges", Slug = "flanges", Icon = "bi-circle", SortOrder = 1, ImagePath = "/img/demo/flange.svg", DescriptionFa = "فلنج‌های گلودار، کور، اسلیپ‌آن و ساکت‌ولد", DescriptionEn = "Weld neck, blind, slip-on and socket weld flanges" };
        var valve = new Category { NameFa = "شیرآلات", NameEn = "Valves", Slug = "valves", Icon = "bi-gear", SortOrder = 2, ImagePath = "/img/demo/valve.svg", DescriptionFa = "شیر کشویی، توپی، سماوری و یک‌طرفه", DescriptionEn = "Gate, ball, globe and check valves" };
        var pipe = new Category { NameFa = "لوله", NameEn = "Pipes", Slug = "pipes", Icon = "bi-distribute-horizontal", SortOrder = 3, ImagePath = "/img/demo/pipe.svg", DescriptionFa = "لوله‌های بدون درز و درزدار کربن استیل و استنلس", DescriptionEn = "Seamless and welded carbon & stainless steel pipes" };
        var fitting = new Category { NameFa = "اتصالات", NameEn = "Fittings", Slug = "fittings", Icon = "bi-bezier2", SortOrder = 4, ImagePath = "/img/demo/fitting.svg", DescriptionFa = "زانو، سه‌راهی، تبدیل و کلاهک", DescriptionEn = "Elbows, tees, reducers and caps" };
        db.Categories.AddRange(flange, valve, pipe, fitting);

        var a105 = new Material { NameFa = "کربن استیل A105", NameEn = "Carbon Steel A105", Code = "A105" };
        var ss316 = new Material { NameFa = "استنلس استیل 316", NameEn = "Stainless Steel 316", Code = "SS316" };
        var api5l = new Material { NameFa = "API 5L Gr.B", NameEn = "API 5L Gr.B", Code = "API5L-B" };
        var wcb = new Material { NameFa = "چدن فولادی WCB", NameEn = "Cast Steel WCB", Code = "WCB" };
        db.Materials.AddRange(a105, ss316, api5l, wcb);

        Product P(Category c, Material m, string code, string slug, string fa, string en, string std, string img, bool featured, bool call = false) => new()
        {
            Category = c, Material = m, Code = code, Slug = slug, NameFa = fa, NameEn = en, Standard = std,
            IsFeatured = featured, CallForPrice = call, Origin = "Iran",
            SummaryFa = $"{fa} با استاندارد {std}، مناسب صنایع نفت، گاز و پتروشیمی.",
            SummaryEn = $"{en} to {std}, for oil, gas and petrochemical industries.",
            DescriptionFa = $"{fa} ساخته‌شده مطابق استاندارد {std}. همراه با گواهی‌نامه مواد (MTC) و دیتاشیت فنی.",
            DescriptionEn = $"{en} manufactured to {std}. Supplied with mill test certificate (MTC) and technical datasheet.",
            Images = [new ProductImage { FilePath = img, IsMain = true, ShowInHomeSlider = featured, AltFa = fa, AltEn = en }],
        };

        var products = new[]
        {
            P(flange, a105, "FL-WN-150", "weld-neck-flange-150", "فلنج گلودار کلاس ۱۵۰", "Weld Neck Flange Class 150", "ASME B16.5", "/img/demo/flange.svg", true),
            P(flange, ss316, "FL-BL-300", "blind-flange-ss316", "فلنج کور استنلس ۳۱۶", "Blind Flange SS316", "ASME B16.5", "/img/demo/flange.svg", false),
            P(valve, wcb, "VL-GT-150", "gate-valve-150", "شیر کشویی کلاس ۱۵۰", "Gate Valve Class 150", "API 600", "/img/demo/valve.svg", true),
            P(valve, ss316, "VL-BL-300", "ball-valve-ss316", "شیر توپی استنلس", "Stainless Ball Valve", "API 6D", "/img/demo/valve.svg", true, call: true),
            P(pipe, api5l, "PP-SML-40", "seamless-pipe-sch40", "لوله بدون درز اسکجول ۴۰", "Seamless Pipe SCH40", "API 5L / ASTM A106", "/img/demo/pipe.svg", true),
            P(fitting, a105, "FT-EL-90", "elbow-90-lr", "زانو ۹۰ درجه بلند", "90° Long Radius Elbow", "ASME B16.9", "/img/demo/fitting.svg", false),
        };
        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        static decimal R(decimal x) => Math.Round(x / 10000m) * 10000m;
        var sizes = new[] { "1/2\"", "1\"", "2\"", "4\"", "6\"" };
        decimal basePrice = 1_200_000;
        foreach (var p in products)
        {
            var i = 0;
            foreach (var s in sizes)
            {
                i++;
                var v = new ProductVariant
                {
                    ProductId = p.Id, Size = s, SortOrder = i,
                    PressureClass = p.Category == pipe ? null : "150#",
                    Schedule = p.Category == pipe || p.Category == fitting ? "SCH40" : null,
                    Unit = p.Category == pipe ? "شاخه" : "عدد",
                    CallForPrice = p.CallForPrice,
                };
                db.ProductVariants.Add(v);
                await db.SaveChangesAsync();
                if (p.CallForPrice) continue;
                var sale = R(basePrice * i * (1 + p.Id * 0.3m));
                var rec = new PriceRecord
                {
                    VariantId = v.Id, PurchasePrice = R(sale * 0.8m), SalePrice = sale,
                    CashPrice = R(sale * 0.97m), ChequePrice = R(sale * 1.05m),
                    ChequeTermDays = 60, DiscountPercent = i == 3 ? 5 : 0, CreatedByName = "سیستم",
                };
                db.PriceRecords.Add(rec);
                v.SalePrice = rec.SalePrice;
                v.DiscountPercent = rec.DiscountPercent;
            }
        }

        for (var i = 1; i <= 8; i++)
            db.Clients.Add(new Client { NameFa = $"شرکت صنعتی نمونه {i}", NameEn = $"Sample Industrial Co. {i}", LogoPath = $"/img/demo/client{(i - 1) % 4 + 1}.svg", SortOrder = i, Industry = "پالایش و پتروشیمی" });

        await db.SaveChangesAsync();
    }
}
