using Limbo.Core.Config;

namespace Limbo.Console.Interactive;

/// <summary>
/// Minimal key-based translator for the interactive menu screens. English is
/// the base/default language (matching the rest of the codebase); Turkish is
/// an optional runtime setting under General Settings. Command-line/scripting
/// output (Commands/*.cs, --help) intentionally stays English-only.
/// </summary>
internal static class Loc
{
    public static Language Current { get; set; } = Language.English;

    public static string T(string key, params object[] args)
    {
        if (!Map.TryGetValue(key, out var pair))
            return key;

        var text = Current == Language.Turkish ? pair.Tr : pair.En;
        return args.Length == 0 ? text : string.Format(text, args);
    }

    private static readonly Dictionary<string, (string En, string Tr)> Map = new()
    {
        ["press_enter"] = ("Press Enter to continue...", "Devam etmek için Enter'a basın..."),
        ["back"] = ("« Back", "« Geri"),

        // Main menu
        ["main_menu.subtitle"] = ("Main Menu", "Ana Menü"),
        ["main_menu.prompt"] = ("What would you like to do?", "Ne yapmak istersiniz?"),
        ["main_menu.start_engine"] = ("Start Engine", "Motoru Başlat"),
        ["main_menu.profiles"] = ("Profile Management", "Profil Yönetimi"),
        ["main_menu.dns"] = ("DNS Settings", "DNS Ayarları"),
        ["main_menu.radar"] = ("Adaptive Engine (Radar)", "Adaptif Motor (Radar)"),
        ["main_menu.calibrate"] = ("Calibration", "Kalibrasyon"),
        ["main_menu.test"] = ("Connection Test", "Bağlantı Testi"),
        ["main_menu.settings"] = ("General Settings", "Genel Ayarlar"),
        ["main_menu.network_analysis"] = ("Network Analysis", "Ağ Analizi"),
        ["main_menu.about"] = ("About", "Hakkında"),
        ["main_menu.help"] = ("How to Use", "Nasıl Kullanılır"),
        ["main_menu.switch_mode"] = ("Switch Mode → {0}", "Modu Değiştir → {0}"),
        ["main_menu.group_engine"] = ("ENGINE", "MOTOR"),
        ["main_menu.group_config"] = ("CONFIGURATION", "YAPILANDIRMA"),
        ["main_menu.group_tools"] = ("TOOLS", "ARAÇLAR"),
        ["status.network"] = ("Network / DNS", "Ağ / DNS"),
        ["status.profiles_radar"] = ("Profiles / Radar", "Profil / Radar"),
        ["status.profiles_radar_value"] = ("{0} profiles · {1} detected · Radar {2}", "{0} profil · {1} tespit · Radar {2}"),
        ["main_menu.exit"] = ("Exit", "Çıkış"),

        // How to use
        ["help.subtitle"] = ("How to Use", "Nasıl Kullanılır"),
        ["help.prompt"] = ("Choose a topic:", "Bir konu seçin:"),
        ["help.topic.quickstart"] = ("Quick start", "Hızlı başlangıç"),
        ["help.topic.modes"] = ("Modes: profiles only vs global", "Modlar: sadece profiller ve genel"),
        ["help.topic.profiles"] = ("Profiles and excluding a site", "Profiller ve bir siteyi hariç tutma"),
        ["help.topic.dns"] = ("DNS over HTTPS", "DNS over HTTPS"),
        ["help.topic.troubleshooting"] = ("A site still won't open", "Bir site hâlâ açılmıyor"),
        ["help.text.quickstart"] = (
            "[bold]1.[/] Run Limbo as Administrator (it asks by itself; WinDivert needs it).\n" +
            "[bold]2.[/] Check the status panel: [green]DNS over HTTPS[/] should be on.\n" +
            "[bold]3.[/] Choose [bold]Start Engine[/], then open the app or site you want to reach.\n\n" +
            "If something still doesn't open, switch to [bold]global mode[/] from the main menu, or use [bold]Calibration[/] to find the best method for that site.\n" +
            "Press [bold]Ctrl+C[/] to stop the engine.",
            "[bold]1.[/] Limbo'yu yönetici olarak çalıştırın (kendisi ister; WinDivert bunu gerektirir).\n" +
            "[bold]2.[/] Durum paneline bakın: [green]DNS over HTTPS[/] açık olmalı.\n" +
            "[bold]3.[/] [bold]Motoru Başlat[/]'ı seçin, ardından ulaşmak istediğiniz uygulamayı ya da siteyi açın.\n\n" +
            "Yine açılmayan bir şey olursa ana menüden [bold]genel moda[/] geçin ya da o site için en iyi yöntemi bulmak üzere [bold]Kalibrasyon[/]'u kullanın.\n" +
            "Motoru durdurmak için [bold]Ctrl+C[/]."),
        ["help.text.modes"] = (
            "[bold]Profiles only[/] (default): Limbo touches only domains that have a profile, or that Radar detected as blocked. Everything else passes untouched - the least intrusive option.\n\n" +
            "[bold]Global[/] (GoodbyeDPI-style): every HTTPS/HTTP connection without a profile gets the default method. Use it when you don't know which sites are blocked, or many of them are.\n\n" +
            "The default method is set in [bold]Adaptive Engine (Radar) → Change Default Method[/]. Switch modes from the main menu, or for a single run with [grey]limbo run --global[/].",
            "[bold]Sadece profiller[/] (varsayılan): Limbo yalnızca profili olan ya da Radar'ın engellendiğini tespit ettiği alan adlarına dokunur. Geri kalan her şey olduğu gibi geçer - en az müdahaleli seçenek.\n\n" +
            "[bold]Genel[/] (GoodbyeDPI gibi): profili olmayan her HTTPS/HTTP bağlantısına varsayılan yöntem uygulanır. Hangi sitelerin engelli olduğunu bilmediğinizde ya da çok sayıda site engelliyse kullanın.\n\n" +
            "Varsayılan yöntem [bold]Adaptif Motor (Radar) → Varsayılan Yöntemi Değiştir[/]'den ayarlanır. Modu ana menüden, tek bir çalıştırma içinse [grey]limbo run --global[/] ile değiştirebilirsiniz."),
        ["help.text.profiles"] = (
            "A profile says which bypass method (and parameters) a domain gets. [grey]*.example.com[/] covers example.com itself and all its subdomains.\n\n" +
            "[bold]Excluding a site:[/] if global mode breaks a site, give it a profile with method [bold]None[/] - in Profile Management, or [grey]limbo add-profile *.site.com None[/]. Profiles always take priority, in both modes.\n\n" +
            "Not sure which method works? [bold]Calibration[/] tests them against the site and can save the best one as a profile.",
            "Profil, bir alan adına hangi bypass yönteminin (ve parametrelerin) uygulanacağını belirler. [grey]*.example.com[/] hem example.com'un kendisini hem de tüm alt alanlarını kapsar.\n\n" +
            "[bold]Bir siteyi hariç tutmak:[/] genel mod bir siteyi bozuyorsa ona yöntemi [bold]None[/] olan bir profil verin - Profil Yönetimi'nden ya da [grey]limbo add-profile *.site.com None[/] ile. Profiller her iki modda da her zaman önceliklidir.\n\n" +
            "Hangi yöntemin çalıştığından emin değil misiniz? [bold]Kalibrasyon[/] yöntemleri siteye karşı dener ve en iyisini profil olarak kaydedebilir."),
        ["help.text.dns"] = (
            "Some ISPs also [bold]poison DNS[/]: a blocked name resolves to a wrong address - often a block page - and the connection goes to the wrong server however well the handshake is hidden.\n\n" +
            "With DoH on, while the engine runs, [bold]every app's DNS queries (IPv4 and IPv6)[/] are answered over an encrypted server instead. System DNS settings aren't changed.\n\n" +
            "On the live dashboard, a growing [bold]DNS via DoH[/] count means it's working; queries shown as [yellow]plain[/] fell back to normal DNS because the DoH server couldn't be reached.\n\n" +
            "Settings: [bold]DNS Settings → Toggle DoH / Choose Provider[/].",
            "Bazı İSS'ler [bold]DNS'i de zehirler[/]: engelli bir ad yanlış bir adrese - çoğu zaman bir engelleme sayfasına - çözülür ve el sıkışma ne kadar iyi gizlenirse gizlensin bağlantı yanlış sunucuya gider.\n\n" +
            "DoH açıkken, motor çalıştığı sürece [bold]tüm uygulamaların DNS sorguları (IPv4 ve IPv6)[/] bunun yerine şifreli bir sunucu üzerinden cevaplanır. Sistem DNS ayarlarına dokunulmaz.\n\n" +
            "Canlı panelde [bold]DNS via DoH[/] sayısı artıyorsa çalışıyor demektir; [yellow]plain[/] olarak görünen sorgular DoH sunucusuna ulaşılamadığı için normal DNS'e dönmüştür.\n\n" +
            "Ayarlar: [bold]DNS Ayarları → DoH Aç/Kapa / Sağlayıcı Seç[/]."),
        ["help.text.troubleshooting"] = (
            "Check in this order:\n\n" +
            "[bold]1. DNS.[/] With the engine running, look the site up in a command prompt: [grey]nslookup example.com[/]. If the address isn't the site's real one (often a block page), DNS is being tampered with - make sure DoH is on.\n" +
            "[bold]2. System DNS settings.[/] Another setting at the system or network level can override the DNS you entered. The DNS in Limbo's status panel is the one the system actually uses.\n" +
            "[bold]3. IPv6.[/] Routers can hand out their own DNS servers over IPv6, which an IPv4-only DNS setting doesn't cover. With DoH on, Limbo covers IPv6 queries too.\n" +
            "[bold]4. Method.[/] Switch to global mode, or run Calibration for that site.\n" +
            "[bold]5. Type of blocking.[/] Network Analysis shows how the connection is being blocked: DNS tampering, RST injection, or silent dropping.",
            "Şu sırayla kontrol edin:\n\n" +
            "[bold]1. DNS.[/] Motor çalışırken komut isteminde siteyi sorgulayın: [grey]nslookup example.com[/]. Dönen adres sitenin gerçek adresi değilse (çoğu zaman bir engelleme sayfası) DNS'e müdahale ediliyordur - DoH'un açık olduğundan emin olun.\n" +
            "[bold]2. Sistem DNS ayarları.[/] Sistem ya da ağ seviyesindeki başka bir ayar, girdiğiniz DNS'i geçersiz kılabilir. Limbo'nun durum panelinde gösterdiği DNS, sistemin gerçekte kullandığıdır.\n" +
            "[bold]3. IPv6.[/] Modemler IPv6 üzerinden kendi DNS sunucularını dağıtabilir; yalnızca IPv4 için yapılmış bir DNS ayarı bunu kapsamaz. DoH açıkken Limbo IPv6 sorgularını da kapsar.\n" +
            "[bold]4. Yöntem.[/] Genel moda geçin ya da o site için Kalibrasyon çalıştırın.\n" +
            "[bold]5. Engelleme türü.[/] Ağ Analizi, bağlantının nasıl engellendiğini gösterir: DNS müdahalesi, RST enjeksiyonu ya da sessiz düşürme."),

        // About + update check
        ["about.subtitle"] = ("About", "Hakkında"),
        ["about.version"] = ("Version", "Sürüm"),
        ["about.license"] = ("License", "Lisans"),
        ["about.source"] = ("Source code", "Kaynak kod"),
        ["about.third_party"] = ("Bundled components", "Paketteki bileşenler"),
        ["about.third_party_value"] = ("WinDivert, Spectre.Console, BouncyCastle, .NET runtime - see THIRD-PARTY-NOTICES.txt", "WinDivert, Spectre.Console, BouncyCastle, .NET runtime - bkz. THIRD-PARTY-NOTICES.txt"),
        ["about.updates"] = ("Updates", "Güncellemeler"),
        ["about.open_release"] = ("Open the download page for {0}", "{0} indirme sayfasını aç"),
        ["about.open_repo"] = ("Open the GitHub page", "GitHub sayfasını aç"),
        ["about.opened"] = ("Opened in your browser: {0}", "Tarayıcıda açıldı: {0}"),
        ["about.open_failed"] = ("Couldn't open a browser - the address is: {0}", "Tarayıcı açılamadı - adres: {0}"),
        ["update.disabled"] = ("Off (can be turned on in General Settings)", "Kapalı (Genel Ayarlar'dan açılabilir)"),
        ["update.checking"] = ("Checking...", "Kontrol ediliyor..."),
        ["update.up_to_date"] = ("Up to date", "Güncel"),
        ["update.available"] = ("New version available: {0}", "Yeni sürüm var: {0}"),
        ["update.failed"] = ("Couldn't check (offline or GitHub unreachable)", "Kontrol edilemedi (internet yok ya da GitHub'a ulaşılamadı)"),
        ["status.update"] = ("Update", "Güncelleme"),
        ["status.update_value"] = ("{0} available - see About", "{0} yayınlandı - bkz. Hakkında"),

        // Status panel
        ["status.admin"] = ("Administrator", "Yönetici"),
        ["status.admin_yes"] = ("✓ Yes", "✓ Evet"),
        ["status.admin_no"] = ("✗ No", "✗ Hayır"),
        ["status.not_found"] = ("(not found)", "(bulunamadı)"),
        ["status.on"] = ("On", "Açık"),
        ["status.off"] = ("Off", "Kapalı"),
        ["status.doh"] = ("DNS over HTTPS", "DNS over HTTPS"),

        // Calibration
        ["calibrate.subtitle"] = ("Calibration", "Kalibrasyon"),
        ["calibrate.domain_prompt"] = ("Domain to calibrate:", "Kalibre edilecek domain:"),
        ["calibrate.port_prompt"] = ("Port:", "Port:"),
        ["calibrate.save_confirm"] = ("Create a profile for *.{0}?", "*.{0} için profil oluşturulsun mu?"),

        // Connection test
        ["test.subtitle"] = ("Connection Test", "Bağlantı Testi"),
        ["test.domain_prompt"] = ("Domain to test:", "Test edilecek domain:"),

        // General settings
        ["settings.subtitle"] = ("General Settings", "Genel Ayarlar"),
        ["settings.filter"] = ("Filter", "Filter"),
        ["settings.verbose"] = ("Verbose", "Verbose"),
        ["settings.log_file"] = ("Log File", "Log Dosyası"),
        ["settings.log_file_none"] = ("(none)", "(yok)"),
        ["settings.config_file"] = ("Config File", "Config Dosyası"),
        ["settings.language"] = ("Language", "Dil"),
        ["settings.update_check"] = ("Update check", "Güncelleme kontrolü"),
        ["settings.toggle_update_check"] = ("Toggle Update Check", "Güncelleme Kontrolünü Aç/Kapa"),
        ["settings.prompt"] = ("What would you like to do?", "Ne yapmak istersiniz?"),
        ["settings.toggle_verbose"] = ("Toggle Verbose", "Verbose Aç/Kapa"),
        ["settings.set_log_file"] = ("Set Log File", "Log Dosyası Ayarla"),
        ["settings.change_language"] = ("Change Language", "Dili Değiştir"),
        ["settings.log_file_prompt"] = ("Log file path (empty = off):", "Log dosyası yolu (boş = kapat):"),
        ["settings.language_prompt"] = ("Choose a language:", "Dil seçin:"),
        ["settings.language_saved"] = ("Language updated.", "Dil güncellendi."),
        ["on"] = ("on", "açık"),
        ["off"] = ("off", "kapalı"),

        // Profile management
        ["profiles.subtitle"] = ("Profile Management", "Profil Yönetimi"),
        ["profiles.prompt"] = ("What would you like to do?", "Ne yapmak istersiniz?"),
        ["profiles.list"] = ("List Profiles", "Profilleri Listele"),
        ["profiles.add"] = ("Add Profile", "Profil Ekle"),
        ["profiles.edit"] = ("Edit Profile", "Profil Düzenle"),
        ["profiles.delete"] = ("Delete Profile", "Profil Sil"),
        ["profiles.list_subtitle"] = ("Profiles", "Profiller"),
        ["profiles.add_subtitle"] = ("Add Profile", "Profil Ekle"),
        ["profiles.edit_subtitle"] = ("Edit Profile: {0}", "Profil Düzenle: {0}"),
        ["profiles.none_to_edit"] = ("No profiles to edit.", "Düzenlenecek profil yok."),
        ["profiles.none_to_delete"] = ("No profiles to delete.", "Silinecek profil yok."),
        ["profiles.select_to_edit"] = ("Select a profile to edit:", "Düzenlenecek profili seçin:"),
        ["profiles.select_to_delete"] = ("Select a profile to delete:", "Silinecek profili seçin:"),
        ["profiles.confirm_delete"] = ("Delete profile '{0}'?", "'{0}' profilini silmek istediğinize emin misiniz?"),
        ["profiles.deleted"] = ("Deleted:", "Silindi:"),
        ["profiles.domain_prompt"] = ("Domain (e.g. [grey]*.discord.com[/]):", "Domain (örn. [grey]*.discord.com[/]):"),
        ["profiles.domain_empty_error"] = ("Domain cannot be empty", "Domain boş olamaz"),
        ["profiles.method_prompt"] = ("Select bypass method:", "Bypass yöntemi seçin:"),
        ["profiles.use_adaptive_prompt"] = ("Use adaptive (auto-tuned) parameters?", "Adaptif (otomatik ayarlanan) parametreler kullanılsın mı?"),
        ["profiles.saved"] = ("Profile saved:", "Profil kaydedildi:"),

        // Method parameters (shared by profile wizard + radar default-method screen)
        ["param.fragment_size"] = ("Fragment size:", "Fragment boyutu:"),
        ["param.split_position"] = ("Split position:", "Split pozisyonu:"),
        ["param.fake_ttl"] = ("Fake TTL value:", "Sahte TTL değeri:"),
        ["param.ech_name"] = ("ECH public name (empty = auto):", "ECH public name (boş = otomatik):"),
        ["param.quic_size"] = ("Fake packet size:", "Sahte paket boyutu:"),

        // DNS settings
        ["dns.subtitle"] = ("DNS Settings", "DNS Ayarları"),
        ["dns.interface"] = ("Active Interface", "Aktif Arayüz"),
        ["dns.system_dns_v4"] = ("System DNS (IPv4)", "Sistem DNS (IPv4)"),
        ["dns.system_dns_v6"] = ("System DNS (IPv6)", "Sistem DNS (IPv6)"),
        ["dns.none"] = ("(none)", "(yok)"),
        ["dns.ipv6_warning"] = ("IPv6 DNS servers are active. A DNS setting made for IPv4 only doesn't cover them, so queries can still go to the router's or ISP's DNS and be tampered with. Turn on DoH, or set your IPv6 DNS servers too.", "IPv6 DNS sunucuları etkin. Yalnızca IPv4 için yapılmış bir DNS ayarı bunları kapsamaz; sorgular yine modemin ya da İSS'nin DNS'ine gidip müdahaleye uğrayabilir. DoH'u açın ya da IPv6 DNS sunucularını da ayarlayın."),
        ["dns.ipv6_covered"] = ("IPv6 DNS servers are active - covered by DoH while the engine runs.", "IPv6 DNS sunucuları etkin - motor çalıştığı sürece DoH tarafından kapsanıyor."),
        ["dns.not_found"] = ("(not found)", "(bulunamadı)"),
        ["dns.doh"] = ("DNS over HTTPS", "DNS over HTTPS"),
        ["dns.server"] = ("DoH Server", "DoH Sunucusu"),
        ["dns.doh_note"] = ("With DoH on, while the engine runs every app's DNS queries (IPv4 and IPv6) are answered over this encrypted server, so the ISP can't poison them. System DNS settings aren't changed; if the server is unreachable, normal DNS is used.", "DoH açıkken motor çalıştığı sürece tüm uygulamaların DNS sorguları (IPv4 ve IPv6) bu şifreli sunucu üzerinden cevaplanır, İSS zehirleyemez. Sistem DNS ayarlarına dokunulmaz; sunucuya ulaşılamazsa normal DNS kullanılır."),
        ["dns.prompt"] = ("What would you like to do?", "Ne yapmak istersiniz?"),
        ["dns.toggle"] = ("Toggle DoH", "DoH Aç/Kapa"),
        ["dns.pick_provider"] = ("Choose Provider", "Sağlayıcı Seç"),
        ["dns.test_now"] = ("Test Now", "Şimdi Test Et"),
        ["dns.enabled"] = ("DoH enabled.", "DoH açıldı."),
        ["dns.disabled"] = ("DoH disabled.", "DoH kapatıldı."),
        ["dns.provider_prompt"] = ("Choose a DoH provider:", "DoH sağlayıcısı seçin:"),
        ["dns.custom_url_prompt"] = ("Enter DoH URL:", "DoH URL girin:"),
        ["dns.custom_option"] = ("Custom URL...", "Özel URL gir..."),
        ["dns.saved"] = ("Saved:", "Kaydedildi:"),
        ["dns.test_domain_prompt"] = ("Domain to test:", "Test edilecek domain:"),

        // Radar / adaptive engine
        ["radar.subtitle"] = ("Adaptive Engine (Radar)", "Adaptif Motor (Radar)"),
        ["radar.status"] = ("Status:", "Durum:"),
        ["radar.detected_count"] = ("Detected domains:", "Tespit edilen domain sayısı:"),
        ["radar.prompt"] = ("What would you like to do?", "Ne yapmak istersiniz?"),
        ["radar.turn_off"] = ("Turn Off Adaptive Engine", "Adaptif Motoru Kapat"),
        ["radar.turn_on"] = ("Turn On Adaptive Engine", "Adaptif Motoru Aç"),
        ["radar.list"] = ("List Detected Domains", "Tespit Edilenleri Listele"),
        ["radar.default_method"] = ("Change Default Method", "Varsayılan Yöntemi Değiştir"),
        ["radar.import"] = ("Import", "İçe Aktar"),
        ["radar.export"] = ("Export", "Dışa Aktar"),
        ["radar.clear"] = ("Clear", "Temizle"),
        ["radar.turned_off"] = ("Adaptive engine turned off. Only manual profiles will be used.", "Adaptif motor kapatıldı. Sadece manuel profiller kullanılacak."),
        ["radar.turned_on"] = ("Adaptive engine turned on.", "Adaptif motor açıldı."),
        ["radar.list_subtitle"] = ("Radar - Detected Domains", "Radar - Tespit Edilenler"),
        ["radar.import_prompt"] = ("File path to import:", "İçe aktarılacak dosya yolu:"),
        ["radar.export_prompt"] = ("File path to export:", "Dışa aktarılacak dosya yolu:"),
        ["radar.default_method_subtitle"] = ("Default Method for Auto-Detected Domains", "Otomatik Tespit Edilen Domainler için Varsayılan Yöntem"),
        ["radar.current_default"] = ("Default method (Radar detections and global mode): {0}", "Varsayılan yöntem (Radar tespitleri ve genel mod): {0}"),
        ["radar.global_on"] = ("Turn On Global Mode (all traffic, like GoodbyeDPI)", "Genel Modu Aç (tüm trafik, GoodbyeDPI gibi)"),
        ["radar.global_off"] = ("Turn Off Global Mode (profiles only)", "Genel Modu Kapat (sadece profiller)"),
        ["radar.global_turned_on"] = ("Global mode on: every HTTPS/HTTP connection without a profile now gets the default method ({0}). Profiles still take priority - a profile with method None excludes a site that global mode breaks.", "Genel mod açıldı: profili olmayan her HTTPS/HTTP bağlantısına artık varsayılan yöntem ({0}) uygulanır. Profiller yine önceliklidir - genel modun bozduğu bir siteyi yöntemi None olan bir profille hariç tutabilirsiniz."),
        ["radar.global_turned_off"] = ("Global mode off: only domains with a profile (or detected by Radar) are modified.", "Genel mod kapatıldı: sadece profili olan (ya da Radar'ın tespit ettiği) domainlere dokunulur."),
        ["status.mode"] = ("Mode", "Mod"),
        ["status.mode_profiles"] = ("Profiles only", "Sadece profiller"),
        ["status.mode_global"] = ("Global - all traffic", "Genel - tüm trafik"),
        ["radar.default_method_saved"] = ("Default method updated.", "Varsayılan yöntem güncellendi."),

        // Network analysis
        ["network.subtitle"] = ("Network Analysis", "Ağ Analizi"),
        ["network.fetching"] = ("Looking up your public IP/ISP...", "Genel IP/ISS bilgisi sorgulanıyor..."),
        ["network.public_ip"] = ("Public IP", "Genel IP"),
        ["network.isp"] = ("ISP / Organization", "ISS / Kuruluş"),
        ["network.location"] = ("Location", "Konum"),
        ["network.not_available"] = ("(not available - no network or lookup service unreachable)", "(alınamadı - ağ yok ya da sorgu servisine ulaşılamadı)"),
        ["network.local_interface"] = ("Local Interface", "Yerel Arayüz"),
        ["network.local_dns"] = ("Local DNS Servers", "Yerel DNS Sunucuları"),
        ["network.domain_prompt"] = ("Domain to scan (empty = skip):", "Taranacak domain (boş = atla):"),
        ["network.intercept_busy"] = ("Another Limbo instance is already intercepting traffic (run, calibration or network scan). Stop it first - the scan would see packets that copy is rewriting.", "Başka bir Limbo kopyası zaten trafiği yakalıyor (çalıştırma, kalibrasyon ya da ağ taraması). Önce onu durdurun - aksi halde tarama o kopyanın değiştirdiği paketleri görür."),
        ["network.requires_admin_note"] = ("Administrator privileges required for the live behavior scan (RST injection / silent drop detection) - showing network identity only.", "Canlı davranış taraması (RST enjeksiyonu / sessiz düşürme tespiti) için yönetici yetkisi gerekiyor - sadece ağ kimliği gösteriliyor."),
        ["network.scanning"] = ("Scanning...", "Taranıyor..."),
        ["network.dns_tamper_yes"] = ("DNS tampering suspected (system DNS differs from DoH)", "DNS tamper şüphesi (sistem DNS, DoH'tan farklı)"),
        ["network.dns_tamper_no"] = ("DNS looks consistent with DoH", "DNS, DoH ile tutarlı görünüyor"),
        ["network.outcome"] = ("Result", "Sonuç"),
        ["network.outcome.reachable"] = ("Reachable - no interference detected", "Ulaşılabilir - müdahale tespit edilmedi"),
        ["network.outcome.rst"] = ("RST injection detected", "RST enjeksiyonu tespit edildi"),
        ["network.outcome.silentdrop"] = ("Silently dropped (no response at all)", "Sessizce düşürüldü (hiç yanıt yok)"),
        ["network.outcome.unreachable"] = ("Unreachable (reason unclear)", "Ulaşılamıyor (sebep belirsiz)"),
        ["network.outcome.dnsfail"] = ("Could not resolve domain", "Domain çözümlenemedi"),
        ["network.outcome.noadmin"] = ("Skipped (requires Administrator)", "Atlandı (Yönetici yetkisi gerekiyor)")
    };
}
