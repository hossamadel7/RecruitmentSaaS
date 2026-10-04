namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Maps the WhatsApp Inbox's Admin/Supervisor/Moderator/SalesAgent permission model onto the
    /// app's existing Role byte codes (no separate WhatsApp role column):
    ///   1 Admin            -> Admin      (full access, manage accounts, assign, view all)
    ///   7 Team leader      -> Supervisor (view all, assign/transfer, monitor)
    ///   8 TeleSales manager -> SalesAgent + supervisor of their own team only (see WhatsAppScope)
    ///   2 Reception        -> Moderator  (create Messenger handoffs)
    ///   6 Sales, 3 TeleSales -> SalesAgent (reply/act only on conversations assigned to them)
    /// Every check here is also meant to be re-verified server-side per request — never trust
    /// the UI alone to hide actions a role shouldn't be able to take.
    /// </summary>
    public static class WhatsAppAuthorization
    {
        public const string AllowedRoles = "1,2,3,6,7,8";

        public static bool IsOrgWide(string? role) => role == "1" || role == "7";

        public static bool IsAdmin(string? role) => role == "1";

        public static bool CanManageAccounts(string? role) => role == "1";

        public static bool CanAssignOrTransfer(string? role) => role == "1" || role == "7" || role == "8";

        public static bool CanCreateHandoff(string? role) => role == "1" || role == "2" || role == "7" || role == "8";

        public static bool CanAccessConversation(string? role, Guid userId, Guid? assignedAgentId) =>
            IsOrgWide(role) || assignedAgentId == userId;

        /// <summary>Supervisors see more than their own chats: org-wide roles, or role 8 for their team.</summary>
        public static bool IsSupervisor(string? role) => IsOrgWide(role) || role == "8";
    }
}
