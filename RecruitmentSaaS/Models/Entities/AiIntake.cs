using System;

namespace RecruitmentSaaS.Models.Entities;

/// <summary>Where the AI WhatsApp assistant is with a chat.</summary>
public enum IntakeStatus : byte
{
    None = 0,
    /// <summary>The assistant is asking for name, age and job.</summary>
    Collecting = 1,
    /// <summary>All collected (or timed out with the age) — lead created and routed.</summary>
    Completed = 2,
    /// <summary>Handed to the team leader (asked for a person, voice notes, error, too long…).</summary>
    HandedOff = 3,
    /// <summary>A person took over the chat (transfer or wrote in it).</summary>
    StoppedByStaff = 4
}

public partial class WhatsAppConversation
{
    public byte IntakeStatus { get; set; }

    public string? IntakeName { get; set; }

    public byte? IntakeAge { get; set; }

    public string? IntakeJob { get; set; }

    public DateTime? IntakeStartedAt { get; set; }

    /// <summary>When the age was given — the clock for "hand over after N minutes with the age".</summary>
    public DateTime? IntakeAgeAt { get; set; }

    public DateTime? IntakeLastCustomerAt { get; set; }

    /// <summary>How many replies the assistant has sent in this chat.</summary>
    public int IntakeTurns { get; set; }

    /// <summary>Customer turns with no text (voice notes, photos) — the assistant can't read them.</summary>
    public int IntakeNoTextCount { get; set; }

    public string? IntakeHandoffReason { get; set; }
}

public partial class WhatsAppAccount
{
    /// <summary>The AI assistant answers new chats on this number (when it's switched on globally).</summary>
    public bool AiAssistantEnabled { get; set; }
}

public partial class LeadFormSetting
{
    public const int DefaultAiWaitAfterAgeMinutes = 60;
    public const int DefaultAiWaitNoAgeHours = 48;

    public bool AiEnabled { get; set; }

    /// <summary>While on, the assistant only answers the numbers in <see cref="AiTestNumbers"/>.</summary>
    public bool AiTestMode { get; set; } = true;

    /// <summary>WhatsApp numbers (international digits) the assistant answers in test mode, comma or line separated.</summary>
    public string? AiTestNumbers { get; set; }

    /// <summary>How the assistant opens the chat.</summary>
    public string? AiGreeting { get; set; }

    /// <summary>Customer gave the age but stopped answering: route them after this many minutes.</summary>
    public int AiWaitAfterAgeMinutes { get; set; }

    /// <summary>Customer never gave the age: hand to the team leader after this many hours.</summary>
    public int AiWaitNoAgeHours { get; set; }

    /// <summary>Under-age WhatsApp customers go to the team's rotation instead of waiting with the team leader.</summary>
    public bool AiUnderAgeToRotation { get; set; }

    /// <summary>The assistant's messages as edited on the admin page (JSON key → text); missing keys use the defaults in AiMessages.</summary>
    public string? AiMessagesJson { get; set; }

    public const string DefaultAiGreeting =
        "أهلاً بحضرتك 👋 أنا المساعد الآلي لشركة الفهد العربي لإلحاق العمالة بالخارج (ترخيص 492). هسأل حضرتك ٣ أسئلة بسيطة وبعدها هحولك لمستشار يكمل معاك.";
}
