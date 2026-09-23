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

        public ConversationsController(
            RecruitmentCrmContext context,
            IWhatsAppCloudApiService cloudApi,
            IInboxRealtimeNotifier notifier)
        {
            _context = context;
            _cloudApi = cloudApi;
            _notifier = notifier;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string CurrentUserName => User.FindFirstValue(ClaimTypes.Name) ?? "مستخدم";
        private string? CurrentRole => User.FindFirstValue(ClaimTypes.Role);
        private bool IsOrgWide => WhatsAppAuthorization.IsOrgWide(CurrentRole);

        // ── GET /api/conversations ────────────────────────────────────────────
        // accountId=<guid|all>  assigned=<me|unassigned|<agentGuid>>  status=<byte>
        // unreadOnly=<bool>  search=<text>  page=<int>
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] Guid? accountId,
            [FromQuery] string? assigned,
            [FromQuery] byte? status,
            [FromQuery] bool unreadOnly = false,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1)
        {
            var userId = CurrentUserId;

            var query = _context.WhatsAppConversations.AsNoTracking().AsQueryable();

            if (!IsOrgWide)
                query = query.Where(c => c.AssignedSalesAgentId == userId);

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
            if (!WhatsAppAuthorization.CanAccessConversation(CurrentRole, CurrentUserId, conversation.AssignedSalesAgentId))
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
            if (!WhatsAppAuthorization.CanAccessConversation(CurrentRole, CurrentUserId, conversation.AssignedSalesAgentId))
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
        [HttpPost("{id:guid}/messages")]
        public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendMessageDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var conversation = await _context.WhatsAppConversations
                .Include(c => c.Contact)
                .Include(c => c.WhatsAppAccount)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!WhatsAppAuthorization.CanAccessConversation(CurrentRole, CurrentUserId, conversation.AssignedSalesAgentId))
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

            await _notifier.NewMessageAsync(message, conversation.AssignedSalesAgentId);
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
            if (!WhatsAppAuthorization.CanAccessConversation(CurrentRole, CurrentUserId, conversation.AssignedSalesAgentId))
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
            var conversation = await _context.WhatsAppConversations.FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();

            var isTakeOver = dto.AgentId == CurrentUserId;
            if (!WhatsAppAuthorization.CanAssignOrTransfer(CurrentRole) && !isTakeOver)
                return Forbid();

            if (dto.AgentId.HasValue)
            {
                var agentExists = await _context.Users.AnyAsync(u => u.Id == dto.AgentId.Value && u.IsActive);
                if (!agentExists) return BadRequest(new { error = "Agent not found or inactive." });
            }

            var previousAgentId = conversation.AssignedSalesAgentId;
            conversation.AssignedSalesAgentId = dto.AgentId;
            conversation.UpdatedAt = DateTime.UtcNow;
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

            return Ok(new { success = true });
        }

        // ── PATCH /api/conversations/{id}/status ──────────────────────────────
        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateConversationStatusDto dto)
        {
            var conversation = await _context.WhatsAppConversations.FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (!WhatsAppAuthorization.CanAccessConversation(CurrentRole, CurrentUserId, conversation.AssignedSalesAgentId))
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
            if (!WhatsAppAuthorization.CanAccessConversation(CurrentRole, CurrentUserId, conversation.AssignedSalesAgentId))
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
            if (!WhatsAppAuthorization.CanAccessConversation(CurrentRole, CurrentUserId, conversation.AssignedSalesAgentId))
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
            if (!WhatsAppAuthorization.CanAccessConversation(CurrentRole, CurrentUserId, conversation.AssignedSalesAgentId))
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
            if (!WhatsAppAuthorization.CanAccessConversation(CurrentRole, CurrentUserId, conversation.AssignedSalesAgentId))
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
