using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers.Api
{
    [Route("api/conversations")]
    [ApiController]
    [Authorize(Roles = WhatsAppAuthorization.AllowedRoles)]
    public class ConversationsController : ControllerBase
    {
        private const int PageSize = 30;
        private const int MessagePageSize = 40;

        private readonly RecruitmentCrmContext _context;
        private readonly IWhatsAppCloudApiService _cloudApi;
        private readonly IInboxRealtimeNotifier _notifier;
        private readonly WhatsAppMediaCache _mediaCache;
        private readonly INotificationService _notifications;

        public ConversationsController(
            RecruitmentCrmContext context,
            IWhatsAppCloudApiService cloudApi,
            IInboxRealtimeNotifier notifier,
            IWebHostEnvironment env,
            IConfiguration config,
            INotificationService notifications)
        {
            _context = context;
            _cloudApi = cloudApi;
            _notifier = notifier;
            _notifications = notifications;
            _mediaCache = WhatsAppMediaCache.For(env, config);
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string CurrentUserName => User.FindFirstValue(ClaimTypes.Name) ?? "مستخدم";
        private string? CurrentRole => User.FindFirstValue(ClaimTypes.Role);
        private bool IsOrgWide => WhatsAppAuthorization.IsOrgWide(CurrentRole);

        // Agents whose chats I may see (null = everyone), loaded once per request
        private List<Guid>? _visible;
        private bool _visibleLoaded;
        private async Task<List<Guid>?> VisibleAgentsAsync()
        {
            if (!_visibleLoaded)
            {
                _visible = await WhatsAppScope.VisibleAgentIdsAsync(_context, CurrentRole, CurrentUserId);
                _visibleLoaded = true;
            }
            return _visible;
        }

        private async Task<bool> CanSeeAsync(Guid? assignedAgentId) =>
            WhatsAppScope.CanSee(await VisibleAgentsAsync(), assignedAgentId);

        // ── GET /api/conversations ────────────────────────────────────────────
        // accountId=<guid|all>  assigned=<me|unassigned|<agentGuid>>  team=<managerGuid>  status=<byte>
        // unreadOnly=<bool>  search=<text>  page=<int>
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] Guid? accountId,
            [FromQuery] string? assigned,
            [FromQuery] Guid? team,
            [FromQuery] byte? status,
            [FromQuery] bool unreadOnly = false,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1)
        {
            var userId = CurrentUserId;

            var query = _context.WhatsAppConversations.AsNoTracking().AsQueryable();

            var visible = await VisibleAgentsAsync();
            if (visible != null)
                query = query.Where(c => c.AssignedSalesAgentId != null && visible.Contains(c.AssignedSalesAgentId.Value));

            if (accountId.HasValue)
                query = query.Where(c => c.WhatsAppAccountId == accountId.Value);

            if (status.HasValue)
                query = query.Where(c => c.Status == status.Value);

            if (unreadOnly)
                query = query.Where(c => c.UnreadCount > 0);

            if (!string.IsNullOrWhiteSpace(assigned))
            {
                if (assigned == "me")
                    query = query.Where(c => c.AssignedSalesAgentId == userId);
                else if (assigned == "unassigned")
                    query = query.Where(c => c.AssignedSalesAgentId == null);
                else if (Guid.TryParse(assigned, out var agentId))
                    query = query.Where(c => c.AssignedSalesAgentId == agentId);
            }

            // All chats of one TeleSales team (agents whose manager is <team>)
            if (team.HasValue)
                query = query.Where(c => c.AssignedSalesAgent != null && c.AssignedSalesAgent.ManagerId == team.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(c =>
                    (c.Contact.Name != null && c.Contact.Name.Contains(s)) ||
                    c.Contact.WhatsAppPhoneNumber.Contains(s) ||
                    (c.Lead != null && c.Lead.LeadCode != null && c.Lead.LeadCode.Contains(s)));
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                .Skip((Math.Max(page, 1) - 1) * PageSize)
                .Take(PageSize)
                .Select(c => new ConversationListItemDto
                {
                    Id = c.Id,
                    ContactName = c.Contact.Name ?? c.Contact.WhatsAppPhoneNumber,
                    ContactPhone = c.Contact.WhatsAppPhoneNumber,
                    WhatsAppAccountId = c.WhatsAppAccountId,
                    WhatsAppAccountName = c.WhatsAppAccount.Name,
                    AssignedSalesAgentId = c.AssignedSalesAgentId,
                    AssignedSalesAgentName = c.AssignedSalesAgent != null ? c.AssignedSalesAgent.FullName : null,
                    Status = c.Status,
                    LeadStage = c.LeadStage,
                    UnreadCount = c.UnreadCount,
                    LastMessageAt = c.LastMessageAt,
                    LeadCode = c.Lead != null ? c.Lead.LeadCode : null,
                    LastMessagePreview = _context.WhatsAppMessages
                        .Where(m => m.ConversationId == c.Id)
                        .OrderByDescending(m => m.CreatedAt)
                        .Select(m => m.TextBody)
                        .FirstOrDefault(),
                    LastMessageDirection = _context.WhatsAppMessages
                        .Where(m => m.ConversationId == c.Id)
                        .OrderByDescending(m => m.CreatedAt)
                        .Select(m => (byte?)m.Direction)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(new { items, totalCount, page, pageSize = PageSize });
        }

        // ── GET /api/conversations/{id} ───────────────────────────────────────
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var conversation = await _context.WhatsAppConversations
                .AsNoTracking()
                .Include(c => c.Contact)
                .Include(c => c.WhatsAppAccount)
                .Include(c => c.AssignedSalesAgent)
                .Include(c => c.Lead)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            var dto = new ConversationDetailDto
            {
                Id = conversation.Id,
                ContactName = conversation.Contact.Name ?? conversation.Contact.WhatsAppPhoneNumber,
                ContactPhone = conversation.Contact.WhatsAppPhoneNumber,
                WhatsAppAccountId = conversation.WhatsAppAccountId,
                WhatsAppAccountName = conversation.WhatsAppAccount.Name,
                AssignedSalesAgentId = conversation.AssignedSalesAgentId,
                AssignedSalesAgentName = conversation.AssignedSalesAgent?.FullName,
                Status = conversation.Status,
                LeadStage = conversation.LeadStage,
                LostReason = conversation.LostReason,
                UnreadCount = conversation.UnreadCount,
                LastMessageAt = conversation.LastMessageAt,
                LeadId = conversation.LeadId,
                LeadFullName = conversation.Lead?.FullName,
                LeadCode = conversation.Lead?.LeadCode,
                CreatedAt = conversation.CreatedAt,
                OpenedAt = conversation.OpenedAt
            };

            return Ok(dto);
        }

        // ── GET /api/conversations/{id}/messages?before=<iso-timestamp> ──────
        // Most recent page first; pass `before` (the oldest CreatedAt currently
        // loaded) to page further back in history as the user scrolls up.
        [HttpGet("{id:guid}/messages")]
        public async Task<IActionResult> Messages(Guid id, [FromQuery] DateTime? before)
        {
            var conversation = await _context.WhatsAppConversations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            var query = _context.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == id);
            if (before.HasValue)
                query = query.Where(m => m.CreatedAt < before.Value);

            var messages = await query
                .OrderByDescending(m => m.CreatedAt)
                .Take(MessagePageSize)
                .Select(m => new WhatsAppMessageDto
                {
                    Id = m.Id,
                    Direction = m.Direction,
                    MessageType = m.MessageType,
                    TextBody = m.TextBody,
                    MediaUrl = m.MediaUrl,
                    HasMedia = m.MediaId != null,
                    Status = m.Status,
                    ErrorMessage = m.ErrorMessage,
                    SenderUserId = m.SenderUserId,
                    SenderUserName = m.SenderUser != null ? m.SenderUser.FullName : null,
                    ReplyToMessageId = m.ReplyToMessageId,
                    WhatsAppTimestamp = m.WhatsAppTimestamp
                })
                .ToListAsync();

            messages.Reverse(); // oldest -> newest for rendering

            return Ok(new { messages, hasMore = messages.Count == MessagePageSize });
        }

        // ── POST /api/conversations/{id}/messages ─────────────────────────────
        // ── GET /api/conversations/{id}/messages/{messageId}/media ───────────
        // Streams a message's photo / voice note / file. Meta's media URLs need our access token,
        // so the browser can't load them directly.
        [HttpGet("{id:guid}/messages/{messageId:guid}/media")]
        public async Task<IActionResult> GetMedia(Guid id, Guid messageId, CancellationToken ct)
        {
            var conversation = await _context.WhatsAppConversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            var mediaId = await _context.WhatsAppMessages.AsNoTracking()
                .Where(m => m.Id == messageId && m.ConversationId == id)
                .Select(m => m.MediaId)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(mediaId)) return NotFound();

            // From the server's own copy when we have one; otherwise from Meta, then keep a copy
            var file = await _mediaCache.TryGetAsync(mediaId, ct);
            if (file == null)
            {
                file = await _cloudApi.DownloadMediaAsync(mediaId, ct);
                if (file == null) return NotFound(new { error = "تعذر تحميل الملف من واتساب (قد يكون انتهت صلاحيته)" });
                await _mediaCache.SaveAsync(mediaId, file, ct);
            }

            Response.Headers.CacheControl = "private, max-age=86400";
            return File(file.Data, file.ContentType, enableRangeProcessing: true); // range = seekable audio/video
        }

        // What WhatsApp accepts (https://developers.facebook.com/docs/whatsapp/cloud-api/reference/media)
        private static readonly Dictionary<string, (string type, long maxBytes)> AllowedMedia = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ("image", 5 * 1024 * 1024),
            ["image/png"] = ("image", 5 * 1024 * 1024),
            ["audio/ogg"] = ("audio", 16 * 1024 * 1024),
            ["audio/mp4"] = ("audio", 16 * 1024 * 1024),
            ["audio/aac"] = ("audio", 16 * 1024 * 1024),
            ["audio/mpeg"] = ("audio", 16 * 1024 * 1024),
            ["audio/amr"] = ("audio", 16 * 1024 * 1024),
            ["video/mp4"] = ("video", 16 * 1024 * 1024),
            ["video/3gpp"] = ("video", 16 * 1024 * 1024),
            ["application/pdf"] = ("document", 16 * 1024 * 1024),
            ["application/msword"] = ("document", 16 * 1024 * 1024),
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ("document", 16 * 1024 * 1024),
            ["application/vnd.ms-excel"] = ("document", 16 * 1024 * 1024),
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ("document", 16 * 1024 * 1024),
            ["text/plain"] = ("document", 16 * 1024 * 1024),
        };

        // ── POST /api/conversations/{id}/media ────────────────────────────────
        // multipart: file, caption? — photo, voice note (audio/ogg opus), video or document
        [HttpPost("{id:guid}/media")]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public async Task<IActionResult> SendMedia(Guid id, IFormFile? file, [FromForm] string? caption, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return BadRequest(new { error = "لم يتم اختيار ملف" });

            var mime = (file.ContentType ?? "").Split(';')[0].Trim();
            if (!AllowedMedia.TryGetValue(mime, out var kind))
                return BadRequest(new { error = $"نوع الملف غير مدعوم في واتساب ({mime})" });
            if (file.Length > kind.maxBytes)
                return BadRequest(new { error = $"حجم الملف أكبر من المسموح ({kind.maxBytes / (1024 * 1024)} ميجا)" });

            var conversation = await _context.WhatsAppConversations
                .Include(c => c.Contact)
                .Include(c => c.WhatsAppAccount)
                .FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
            var fileName = Path.GetFileName(file.FileName);
            var now = DateTime.UtcNow;

            var message = new WhatsAppMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                WhatsAppAccountId = conversation.WhatsAppAccountId,
                Direction = (byte)MessageDirection.Outgoing,
                MessageType = (byte)(kind.type switch
                {
                    "image" => WhatsAppMessageType.Image,
                    "audio" => WhatsAppMessageType.Audio,
                    "video" => WhatsAppMessageType.Video,
                    _ => WhatsAppMessageType.Document
                }),
                MessageSource = (byte)MessageSource.CloudApi,
                TextBody = kind.type == "document" ? (caption ?? fileName) : caption,
                SenderUserId = CurrentUserId,
                Status = (byte)MessageStatus.Queued,
                WhatsAppTimestamp = now,
                CreatedAt = now
            };
            _context.WhatsAppMessages.Add(message);

            conversation.LastMessageAt = now;
            conversation.UpdatedAt = now;
            if (conversation.Status == (byte)ConversationStatus.New || conversation.Status == (byte)ConversationStatus.Open)
                conversation.Status = (byte)ConversationStatus.WaitingForCustomer;
            await _context.SaveChangesAsync(ct);

            byte[] data;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, ct);
                data = ms.ToArray();
            }

            var phoneNumberId = conversation.WhatsAppAccount.PhoneNumberId;
            var upload = await _cloudApi.UploadMediaAsync(phoneNumberId, data, mime, string.IsNullOrWhiteSpace(fileName) ? "file" : fileName, ct);
            var result = upload.Success && upload.MediaId != null
                ? await _cloudApi.SendMediaMessageAsync(phoneNumberId, conversation.Contact.WhatsAppPhoneNumber, kind.type, upload.MediaId, caption, fileName, ct)
                : upload;

            message.MediaId = upload.MediaId; // lets us show our own sent photo / voice note back in the chat
            if (upload.MediaId != null) // we already have the bytes — no need to fetch our own file back from Meta later
                await _mediaCache.SaveAsync(upload.MediaId, new WhatsAppMediaFile { Data = data, ContentType = mime }, ct);
            message.Status = result.Success ? (byte)MessageStatus.Sent : (byte)MessageStatus.Failed;
            message.WhatsAppMessageId = result.WhatsAppMessageId;
            message.ErrorCode = result.ErrorCode;
            message.ErrorMessage = result.ErrorMessage;

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = CurrentUserId,
                ActorType = 1,
                EventType = "MediaSent",
                EntityType = "WhatsAppConversation",
                EntityId = conversation.Id,
                NewValueJson = $"{{\"type\":\"{kind.type}\",\"mime\":\"{mime}\",\"bytes\":{file.Length}}}",
                CreatedAt = now
            });
            await _context.SaveChangesAsync(ct);

            await _notifier.NewMessageAsync(message, conversation.AssignedSalesAgentId, CurrentUserName);
            if (!result.Success)
                await _notifier.MessageStatusUpdatedAsync(message.Id, conversation.Id, message.Status, message.ErrorMessage);

            return Ok(new WhatsAppMessageDto
            {
                Id = message.Id,
                Direction = message.Direction,
                MessageType = message.MessageType,
                TextBody = message.TextBody,
                HasMedia = message.MediaId != null,
                Status = message.Status,
                ErrorMessage = message.ErrorMessage,
                SenderUserId = message.SenderUserId,
                SenderUserName = CurrentUserName,
                WhatsAppTimestamp = message.WhatsAppTimestamp
            });
        }

        [HttpPost("{id:guid}/messages")]
        public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendMessageDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var conversation = await _context.WhatsAppConversations
                .Include(c => c.Contact)
                .Include(c => c.WhatsAppAccount)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            var now = DateTime.UtcNow;

            var message = new WhatsAppMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                WhatsAppAccountId = conversation.WhatsAppAccountId,
                Direction = (byte)MessageDirection.Outgoing,
                MessageType = (byte)WhatsAppMessageType.Text,
                MessageSource = (byte)MessageSource.CloudApi,
                TextBody = dto.Text,
                SenderUserId = CurrentUserId,
                Status = (byte)MessageStatus.Queued,
                WhatsAppTimestamp = now,
                CreatedAt = now
            };
            _context.WhatsAppMessages.Add(message);

            var isFirstAgentReply = !await _context.WhatsAppMessages
                .AnyAsync(m => m.ConversationId == conversation.Id && m.Direction == (byte)MessageDirection.Outgoing);

            conversation.LastMessageAt = now;
            conversation.UpdatedAt = now;
            if (conversation.Status == (byte)ConversationStatus.New || conversation.Status == (byte)ConversationStatus.Open)
                conversation.Status = (byte)ConversationStatus.WaitingForCustomer;

            await _context.SaveChangesAsync();

            var result = await _cloudApi.SendTextMessageAsync(
                conversation.WhatsAppAccount.PhoneNumberId,
                conversation.Contact.WhatsAppPhoneNumber,
                dto.Text);

            message.Status = result.Success ? (byte)MessageStatus.Sent : (byte)MessageStatus.Failed;
            message.WhatsAppMessageId = result.WhatsAppMessageId;
            message.ErrorCode = result.ErrorCode;
            message.ErrorMessage = result.ErrorMessage;
            await _context.SaveChangesAsync();

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = CurrentUserId,
                ActorType = 1,
                EventType = "MessageSent",
                EntityType = "WhatsAppConversation",
                EntityId = conversation.Id,
                CreatedAt = now
            });
            await _context.SaveChangesAsync();

            await _notifier.NewMessageAsync(message, conversation.AssignedSalesAgentId, CurrentUserName);
            if (!result.Success)
                await _notifier.MessageStatusUpdatedAsync(message.Id, conversation.Id, message.Status, message.ErrorMessage);

            return Ok(new WhatsAppMessageDto
            {
                Id = message.Id,
                Direction = message.Direction,
                MessageType = message.MessageType,
                TextBody = message.TextBody,
                Status = message.Status,
                ErrorMessage = message.ErrorMessage,
                SenderUserId = message.SenderUserId,
                SenderUserName = CurrentUserName,
                WhatsAppTimestamp = message.WhatsAppTimestamp
            });
        }

        // ── POST /api/conversations/{id}/read ─────────────────────────────────
        // Marks the conversation read in our CRM. Distinct from WhatsApp's own read receipts.
        [HttpPost("{id:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            var conversation = await _context.WhatsAppConversations.FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            if (conversation.UnreadCount != 0)
            {
                conversation.UnreadCount = 0;
                conversation.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _notifier.UnreadCountUpdatedAsync(conversation.Id, conversation.WhatsAppAccountId, 0, conversation.AssignedSalesAgentId);
            }

            return Ok(new { success = true });
        }

        // ── POST /api/conversations/{id}/assign ───────────────────────────────
        // Also covers transfer (new AgentId), unassign (null AgentId) and "take over" (AgentId = self).
        [HttpPost("{id:guid}/assign")]
        public async Task<IActionResult> Assign(Guid id, [FromBody] AssignConversationDto dto)
        {
            // Only admin, team leaders and the head move chats (a salesperson can't grab one by id)
            if (!WhatsAppAuthorization.CanAssignOrTransfer(CurrentRole))
                return Forbid();

            var conversation = await _context.WhatsAppConversations.FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();
            if (dto.AgentId.HasValue && !await CanSeeAsync(dto.AgentId))
                return BadRequest(new { error = "تقدر تحوّل بس لموظفين في فريقك" });

            User? agent = null;
            if (dto.AgentId.HasValue)
            {
                // Someone who works chats: TeleSales, office Sales or the head TeleSales
                agent = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.AgentId.Value && u.IsActive
                                                                  && (u.Role == 3 || u.Role == 6 || u.Role == 8));
                if (agent == null) return BadRequest(new { error = "الموظف ده غير متاح لاستلام المحادثات" });
            }

            var previousAgentId = conversation.AssignedSalesAgentId;
            if (previousAgentId == dto.AgentId) return Ok(new { success = true, leadMoved = false });

            conversation.AssignedSalesAgentId = dto.AgentId;
            conversation.UpdatedAt = DateTime.UtcNow;

            // The customer's lead follows the chat, so the new person can open and work it
            // (unless the lead is deliberately held by someone else)
            var leadMoved = false;
            Lead? lead = null;
            if (agent != null && conversation.LeadId != null)
            {
                lead = await _context.Leads.FirstOrDefaultAsync(l => l.Id == conversation.LeadId);
                if (lead != null && !lead.IsConverted && lead.AssignedSalesId != agent.Id
                    && (lead.AssignedSalesId == null || lead.AssignedSalesId == previousAgentId))
                {
                    var previousName = previousAgentId == null ? null
                        : await _context.Users.Where(u => u.Id == previousAgentId).Select(u => u.FullName).FirstOrDefaultAsync();
                    lead.AssignedSalesId = agent.Id;
                    lead.UpdatedAt = DateTime.UtcNow;
                    _context.LeadActivities.Add(new LeadActivity
                    {
                        Id = Guid.NewGuid(),
                        LeadId = lead.Id,
                        ActivityType = 8,
                        Description = previousName == null
                            ? $"تم تعيين العميل ومحادثة الواتساب لـ {agent.FullName} بواسطة {CurrentUserName}"
                            : $"تم تحويل العميل ومحادثة الواتساب من {previousName} إلى {agent.FullName} بواسطة {CurrentUserName}",
                        CreatedById = CurrentUserId,
                        CreatedByName = CurrentUserName,
                        ActorType = 1,
                        CreatedAt = DateTime.UtcNow
                    });
                    leadMoved = true;
                }
            }
            await _context.SaveChangesAsync();

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = CurrentUserId,
                ActorType = 1,
                EventType = previousAgentId == null ? "ConversationAssigned" : "ConversationTransferred",
                EntityType = "WhatsAppConversation",
                EntityId = conversation.Id,
                OldValueJson = previousAgentId?.ToString(),
                NewValueJson = dto.AgentId?.ToString(),
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            await _notifier.ConversationAssignedAsync(conversation.Id, conversation.WhatsAppAccountId, previousAgentId, dto.AgentId);

            if (agent != null && agent.Id != CurrentUserId)
            {
                try
                {
                    var customer = await _context.WhatsAppContacts.AsNoTracking()
                        .Where(c => c.Id == conversation.ContactId)
                        .Select(c => c.Name ?? c.WhatsAppPhoneNumber).FirstOrDefaultAsync();
                    await _notifications.SendAsync(agent.Id, "تم تحويل محادثة واتساب لك",
                        $"{customer} — بواسطة {CurrentUserName}", link: $"/Inbox/Index?c={conversation.Id}");
                }
                catch { /* the transfer is saved; a failed notification must not undo it */ }
            }

            return Ok(new { success = true, leadMoved });
        }

        // ── PATCH /api/conversations/{id}/status ──────────────────────────────
        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateConversationStatusDto dto)
        {
            var conversation = await _context.WhatsAppConversations.FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            conversation.Status = dto.Status;
            conversation.UpdatedAt = DateTime.UtcNow;
            if (dto.Status == (byte)ConversationStatus.Closed)
                conversation.ClosedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _notifier.ConversationUpdatedAsync(conversation.Id, conversation.WhatsAppAccountId, conversation.AssignedSalesAgentId);

            return Ok(new { success = true });
        }

        // ── PATCH /api/conversations/{id}/leadstage ───────────────────────────
        [HttpPatch("{id:guid}/leadstage")]
        public async Task<IActionResult> UpdateLeadStage(Guid id, [FromBody] UpdateLeadStageDto dto)
        {
            if (dto.LeadStage == (byte)LeadStage.Lost && string.IsNullOrWhiteSpace(dto.LostReason))
                return BadRequest(new { error = "سبب الخسارة مطلوب" });

            var conversation = await _context.WhatsAppConversations.FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            var previousStage = conversation.LeadStage;
            conversation.LeadStage = dto.LeadStage;
            conversation.LostReason = dto.LeadStage == (byte)LeadStage.Lost ? dto.LostReason : null;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = CurrentUserId,
                ActorType = 1,
                EventType = "LeadStageChanged",
                EntityType = "WhatsAppConversation",
                EntityId = conversation.Id,
                OldValueJson = previousStage.ToString(),
                NewValueJson = dto.LeadStage.ToString(),
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            await _notifier.ConversationUpdatedAsync(conversation.Id, conversation.WhatsAppAccountId, conversation.AssignedSalesAgentId);

            return Ok(new { success = true });
        }

        // ── GET /api/conversations/{id}/notes ─────────────────────────────────
        [HttpGet("{id:guid}/notes")]
        public async Task<IActionResult> GetNotes(Guid id)
        {
            var conversation = await _context.WhatsAppConversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            var notes = await _context.ConversationNotes
                .AsNoTracking()
                .Where(n => n.ConversationId == id)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new ConversationNoteDto
                {
                    Id = n.Id,
                    Body = n.Body,
                    AuthorName = n.Author.FullName,
                    CreatedAt = n.CreatedAt
                })
                .ToListAsync();

            return Ok(notes);
        }

        // ── POST /api/conversations/{id}/notes ────────────────────────────────
        [HttpPost("{id:guid}/notes")]
        public async Task<IActionResult> AddNote(Guid id, [FromBody] AddConversationNoteDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var conversation = await _context.WhatsAppConversations.FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            var note = new ConversationNote
            {
                Id = Guid.NewGuid(),
                ConversationId = id,
                AuthorId = CurrentUserId,
                Body = dto.Body,
                CreatedAt = DateTime.UtcNow
            };
            _context.ConversationNotes.Add(note);

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = CurrentUserId,
                ActorType = 1,
                EventType = "InternalNoteAdded",
                EntityType = "WhatsAppConversation",
                EntityId = conversation.Id,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return Ok(new ConversationNoteDto
            {
                Id = note.Id,
                Body = note.Body,
                AuthorName = CurrentUserName,
                CreatedAt = note.CreatedAt
            });
        }

        // ── POST /api/conversations/{id}/followups ────────────────────────────
        [HttpPost("{id:guid}/followups")]
        public async Task<IActionResult> CreateFollowUp(Guid id, [FromBody] CreateFollowUpDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var conversation = await _context.WhatsAppConversations.FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!await CanSeeAsync(conversation.AssignedSalesAgentId))
                return Forbid();

            var assignedToId = dto.AssignedToId ?? conversation.AssignedSalesAgentId ?? CurrentUserId;

            var followUp = new ConversationFollowUp
            {
                Id = Guid.NewGuid(),
                ConversationId = id,
                AssignedToId = assignedToId,
                CreatedById = CurrentUserId,
                DueAt = dto.DueAt,
                Status = (byte)FollowUpStatus.Pending,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };
            _context.ConversationFollowUps.Add(followUp);

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = CurrentUserId,
                ActorType = 1,
                EventType = "FollowUpCreated",
                EntityType = "WhatsAppConversation",
                EntityId = conversation.Id,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return Ok(new { success = true, id = followUp.Id });
        }
    }
}
