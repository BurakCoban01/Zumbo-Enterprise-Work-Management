using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.Modules.Identity;

internal static class IdentityPermissionSeedDefinitions
{
    internal const int Version = 2;

    internal static IReadOnlyList<Definition> All { get; } =
    [
        D(PermissionCatalog.ProfileRead, "Profili görüntüle", "Kendi profil ve oturum bilgilerini görüntüler.", "Hesap", "System", 10),
        D(PermissionCatalog.OrganizationView, "Organizasyonu görüntüle", "Aktif organizasyonun bilgilerini ve yapısını görüntüler.", "Organizasyon", "System", 15),
        D(PermissionCatalog.OrganizationManage, "Organizasyonu yönet", "Organizasyon ayarlarını ve yapısını yönetir.", "Organizasyon", "System", 20),
        D(PermissionCatalog.UserRoleManage, "Rolleri ve atamaları yönet", "Rol tanımlarını ve kullanıcı rol atamalarını yönetir.", "Erişim", "System", 30),
        D(PermissionCatalog.AuditRead, "Denetim kayıtlarını görüntüle", "Aktif organizasyonun yetkili denetim kayıtlarını görüntüler.", "Denetim", "System", 35),
        D(PermissionCatalog.AuditReadAll, "Tüm denetim kayıtlarını görüntüle", "Yetkili kapsamlardaki denetim kayıtlarını görüntüler.", "Denetim", "System", 40),
        D(PermissionCatalog.OperationsManage, "Operasyonları yönet", "Arama, mesajlaşma ve bakım operasyonlarını yönetir.", "Operasyon", "System", 45),
        D(PermissionCatalog.IntegrationManage, "Entegrasyonları yönet", "Entegrasyon ve webhook yapılandırmasını yönetir.", "Entegrasyon", "System", 50),
        D(PermissionCatalog.NotificationView, "Bildirimleri görüntüle", "Kullanıcı bildirimlerini ve tercihlerini görüntüler.", "Bildirimler", "System", 55),
        D(PermissionCatalog.NotificationManage, "Bildirimleri yönet", "Bildirim durumlarını ve tercihlerini yönetir.", "Bildirimler", "System", 56),
        D(PermissionCatalog.TeamView, "Takımları görüntüle", "Organizasyon takımlarını ve üyeliklerini görüntüler.", "Takımlar", "System", 60),
        D(PermissionCatalog.TeamManage, "Takımları yönet", "Takım yapılandırmasını, davetleri ve üyelikleri yönetir.", "Takımlar", "System", 70),
        D(PermissionCatalog.ProjectView, "Projeleri görüntüle", "Erişilebilir projeleri ve proje ayrıntılarını görüntüler.", "Projeler", "Project", 80),
        D(PermissionCatalog.ProjectManage, "Projeleri yönet", "Proje yapılandırmasını, üyeleri ve sürüm planını yönetir.", "Projeler", "Project", 90),
        D(PermissionCatalog.BoardView, "Panoları görüntüle", "Proje panolarını görüntüler.", "Panolar", "Project", 100),
        D(PermissionCatalog.BoardManage, "Panoları yönet", "Pano sütunlarını ve yapılandırmasını yönetir.", "Panolar", "Project", 110),
        D(PermissionCatalog.WorkflowView, "İş akışlarını görüntüle", "Yayınlanmış iş akışı tanımlarını görüntüler.", "İş akışı", "Project", 120),
        D(PermissionCatalog.WorkflowManage, "İş akışlarını yönet", "İş akışı durum ve geçişlerini yönetir.", "İş akışı", "Project", 130),
        D(PermissionCatalog.WorkItemView, "İşleri görüntüle", "Proje işlerini ve ayrıntılarını görüntüler.", "İşler", "Project", 200),
        D(PermissionCatalog.WorkItemCreate, "İş oluştur", "Projede yeni iş oluşturur.", "İşler", "Project", 210),
        D(PermissionCatalog.WorkItemUpdate, "İşleri düzenle", "İş alanlarını ve planlama bilgilerini düzenler.", "İşler", "Project", 220),
        D(PermissionCatalog.WorkItemAssign, "İş ataması yap", "İşleri proje üyelerine atar.", "İşler", "Project", 230),
        D(PermissionCatalog.WorkItemMove, "İşleri taşı", "İşlerin durum ve sıralamasını değiştirir.", "İşler", "Project", 240),
        D(PermissionCatalog.WorkItemDelete, "İşleri arşivle", "İşleri arşivler veya siler.", "İşler", "Project", 250),
        D(PermissionCatalog.WorkItemLink, "İş bağlantılarını yönet", "İşler arasında bağlantı kurar ve kaldırır.", "İşler", "Project", 260),
        D(PermissionCatalog.WorkItemApprove, "İş onayla", "Onay gerektiren iş kararlarını verir.", "İşler", "Project", 270),
        D(PermissionCatalog.CommentCreate, "Yorumları yönet", "İş yorumları ekler ve yönetir.", "İşbirliği", "Project", 300),
        D(PermissionCatalog.AttachmentCreate, "Ek yükle", "İşlere dosya eki yükler.", "İşbirliği", "Project", 310),
        D(PermissionCatalog.AttachmentDelete, "Ek kaldır", "İşlerden dosya eki kaldırır.", "İşbirliği", "Project", 320),
        D(PermissionCatalog.WorkLogCreate, "Çalışma kaydı ekle", "İşlere çalışma süresi kaydeder.", "İşbirliği", "Project", 330),
        D(PermissionCatalog.ReleaseApprove, "Sürüm onayla", "Sürüm kararlarını onaylar.", "Sürümler", "System", 400),
        D(PermissionCatalog.ReleasePublish, "Sürüm yayınla", "Onaylanmış sürümleri yayınlar.", "Sürümler", "System", 410)
    ];

    private static Definition D(
        string key,
        string label,
        string description,
        string category,
        string scope,
        int order) => new(key, label, description, category, scope, order);

    internal sealed record Definition(
        string Key,
        string Label,
        string Description,
        string Category,
        string Scope,
        int DisplayOrder);
}
