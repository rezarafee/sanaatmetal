using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanaatMetal.Data.Migrations
{
    /// <inheritdoc />
    public partial class SiteAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundImagePath",
                table: "SiteSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackgroundMode",
                table: "SiteSettings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "cover");

            migrationBuilder.AddColumn<int>(
                name: "BackgroundVeil",
                table: "SiteSettings",
                type: "int",
                nullable: false,
                defaultValue: 85);

            migrationBuilder.AddColumn<string>(
                name: "FaviconPath",
                table: "SiteSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FooterLogoPath",
                table: "SiteSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeroDecor",
                table: "SiteSettings",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "blueprint");

            migrationBuilder.AddColumn<string>(
                name: "HeroImagePath",
                table: "SiteSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HoverEffect",
                table: "SiteSettings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "zoom");

            migrationBuilder.AddColumn<int>(
                name: "LogoHeight",
                table: "SiteSettings",
                type: "int",
                nullable: false,
                defaultValue: 44);

            migrationBuilder.AddColumn<string>(
                name: "LogoShape",
                table: "SiteSettings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "circle");

            migrationBuilder.AddColumn<string>(
                name: "OgImagePath",
                table: "SiteSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductImageFit",
                table: "SiteSettings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "contain");

            migrationBuilder.AddColumn<string>(
                name: "ProductImageRatio",
                table: "SiteSettings",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "4:3");

            migrationBuilder.AddColumn<string>(
                name: "RevealAnimation",
                table: "SiteSettings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "fade-up");

            migrationBuilder.AddColumn<bool>(
                name: "ShowBrandText",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowCategories",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowClients",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowDownloads",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowFeatured",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowHeroSearch",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowStats",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowWhyUs",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TextOverrides",
                table: "SiteSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeAccent",
                table: "SiteSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: false,
                defaultValue: "#f5b301");

            migrationBuilder.AddColumn<string>(
                name: "ThemeBackground",
                table: "SiteSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: false,
                defaultValue: "#f3f4f8");

            migrationBuilder.AddColumn<string>(
                name: "ThemeFooterBg",
                table: "SiteSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeHeroBg",
                table: "SiteSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeMode",
                table: "SiteSettings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "auto");

            migrationBuilder.AddColumn<string>(
                name: "ThemeNavBg",
                table: "SiteSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeNavText",
                table: "SiteSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemePrimary",
                table: "SiteSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: false,
                defaultValue: "#22318a");

            migrationBuilder.AddColumn<int>(
                name: "ThemeRadius",
                table: "SiteSettings",
                type: "int",
                nullable: false,
                defaultValue: 18);

            migrationBuilder.AddColumn<string>(
                name: "ThemeTitleFont",
                table: "SiteSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Lalezar");

            migrationBuilder.AddColumn<string>(
                name: "ThemeTopbarBg",
                table: "SiteSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearsOfExperience",
                table: "SiteSettings",
                type: "int",
                nullable: false,
                defaultValue: 15);
            // سایت فعلی (لوله و فلنج) متن‌های قبلی‌اش را نگه می‌دارد؛ نصب‌های تازه متن‌های عمومی می‌گیرند
            migrationBuilder.Sql(@"UPDATE SiteSettings SET TextOverrides = N'{""HeroQ1"":{""fa"":""فلنج"",""en"":""Flange""},""HeroQ2"":{""fa"":""شیر توپی"",""en"":""Ball valve""},""HeroQ3"":{""fa"":""لوله"",""en"":""Pipe""},""OurClientsSub"":{""fa"":""افتخار همکاری با پالایشگاه‌ها و صنایع بزرگ کشور"",""en"":""Trusted by refineries and major industries""},""Why1"":{""fa"":""کالای استاندارد و دارای گواهی"",""en"":""Certified, standard products""},""Why1d"":{""fa"":""همه محصولات با گواهی‌نامه‌های معتبر و دیتاشیت به‌روز عرضه می‌شوند."",""en"":""Every product ships with valid certificates and up-to-date datasheets.""},""Why2"":{""fa"":""تنوع سایز و متریال"",""en"":""Wide range of sizes & materials""},""Why2d"":{""fa"":""از کربن استیل تا استنلس و آلیاژی، در کلاس‌ها و اسکجول‌های مختلف."",""en"":""From carbon steel to stainless and alloys, in many classes and schedules.""},""Why3"":{""fa"":""تحویل سریع پروژه‌ای"",""en"":""Fast project delivery""},""Why3d"":{""fa"":""تأمین سریع اقلام پروژه‌های نفت، گاز و پتروشیمی."",""en"":""Rapid supply for oil, gas and petrochemical projects.""},""Why4"":{""fa"":""مشاوره فنی"",""en"":""Technical consulting""},""Why4d"":{""fa"":""کارشناسان ما در انتخاب کالای مناسب کنار شما هستند."",""en"":""Our engineers help you pick the right item.""},""StatClients"":{""fa"":""مشتری صنعتی"",""en"":""Industrial clients""},""Downloads"":{""fa"":""گواهی‌ها و دیتاشیت‌ها"",""en"":""Certificates & datasheets""},""ProductsMetaDesc"":{""fa"":""لیست محصولات صنعتی: لوله، فلنج، شیرآلات و اتصالات با سایزها، متریال‌ها و استانداردهای مختلف همراه با گواهی و دیتاشیت."",""en"":""Industrial products: pipes, flanges, valves and fittings in many sizes, materials and standards, with certificates and datasheets.""},""DownloadsMetaDesc"":{""fa"":""دانلود گواهی‌نامه‌ها، دیتاشیت‌ها و کاتالوگ محصولات صنعتی (لوله، فلنج، شیرآلات و اتصالات)."",""en"":""Download certificates, datasheets and catalogs for our industrial products (pipes, flanges, valves and fittings).""}}' WHERE TextOverrides IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundImagePath",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BackgroundMode",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BackgroundVeil",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "FaviconPath",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "FooterLogoPath",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HeroDecor",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HeroImagePath",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HoverEffect",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "LogoHeight",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "LogoShape",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "OgImagePath",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ProductImageFit",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ProductImageRatio",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "RevealAnimation",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ShowBrandText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ShowCategories",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ShowClients",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ShowDownloads",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ShowFeatured",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ShowHeroSearch",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ShowStats",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ShowWhyUs",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "TextOverrides",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemeAccent",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemeBackground",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemeFooterBg",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemeHeroBg",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemeMode",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemeNavBg",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemeNavText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemePrimary",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemeRadius",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemeTitleFont",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemeTopbarBg",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "YearsOfExperience",
                table: "SiteSettings");
        }
    }
}
