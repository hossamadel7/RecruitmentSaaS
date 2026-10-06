using System.Text.Json;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Every message the WhatsApp assistant sends, editable from the "المساعد الآلي" page.
    /// Edited texts are stored in <see cref="LeadFormSetting.AiMessagesJson"/>; an empty box means the default below.
    /// Placeholders: {الاسم} = customer's first name, {الموظف} = the TeleSales the chat went to.
    /// </summary>
    public static class AiMessages
    {
        public const string NamePlaceholder = "{الاسم}";
        public const string AgentPlaceholder = "{الموظف}";

        public sealed record Definition(string Key, string Label, string Default, string? Hint = null);

        public const string AskName = "AskName";
        public const string AskNameAgain = "AskNameAgain";
        public const string AskAge = "AskAge";
        public const string AskAgeAgain = "AskAgeAgain";
        public const string AskJob = "AskJob";
        public const string AskJobAgain = "AskJobAgain";
        public const string ConsultantWillExplain = "ConsultantWillExplain";
        public const string NoVoice = "NoVoice";
        public const string DoneAssigned = "DoneAssigned";
        public const string DoneWaiting = "DoneWaiting";
        public const string HandOff = "HandOff";

        public static readonly IReadOnlyList<Definition> All = new[]
        {
            new Definition(AskName, "سؤال الاسم (بعد أول رسالة)", "اسم حضرتك إيه؟ (الاسم ثلاثي لو سمحت)"),
            new Definition(AskNameAgain, "لو ما فهمش الاسم", "ممكن تكتبلي اسم حضرتك؟ 🙏"),
            new Definition(AskAge, "سؤال السن", "تمام يا أ/ {الاسم} 🙏 حضرتك عندك كام سنة؟ (اكتب الرقم بس، مثال: 45)", "{الاسم} = الاسم الأول للعميل"),
            new Definition(AskAgeAgain, "لو ما فهمش السن", "معلش اكتبلي السن بالأرقام بس، مثال: 45"),
            new Definition(AskJob, "سؤال الوظيفة", "حضرتك عايز تشتغل إيه؟ (مثال: سواق، نجار، فني كهرباء)"),
            new Definition(AskJobAgain, "لو ما فهمش الوظيفة", "ممكن تكتبلي الوظيفة اللي حضرتك عايزها؟ (مثال: سواق، نجار)"),
            new Definition(ConsultantWillExplain, "لو العميل سأل سؤال (مرتب، فيزا، مصاريف…) قبل ما يخلص", "المستشار هيوضح لحضرتك كل التفاصيل بعد ما نخلص الأسئلة دي 🙏", "بيتبعت وبعده السؤال اللي عليه الدور"),
            new Definition(NoVoice, "لو بعت رسالة صوتية أو صورة", "معلش يا فندم 🙏 مش بقدر أسمع الرسايل الصوتية أو أشوف الصور — ممكن تكتبلي الرد كتابة؟"),
            new Definition(DoneAssigned, "خلص واتوزع على تيلي سيلز (السن فوق الحد)", "شكراً يا أ/ {الاسم} 🙏 معاك أ/ {الموظف} هتكمل مع حضرتك دلوقتي.", "{الاسم} = اسم العميل، {الموظف} = اسم التيلي سيلز"),
            new Definition(DoneWaiting, "خلص ومستني مسؤول الفريق (السن تحت الحد)", "شكراً يا أ/ {الاسم} 🙏 هيتواصل مع حضرتك مستشار من فريقنا قريب.", "{الاسم} = اسم العميل"),
            new Definition(HandOff, "تحويل لمسؤول الفريق (طلب موظف، مش مهتم، رسايل صوتية…)", "تمام 🙏 هحول حضرتك لمسؤول الفريق وهيرد عليك في أقرب وقت."),
        };

        private static readonly Dictionary<string, Definition> ByKey = All.ToDictionary(d => d.Key);

        /// <summary>The edited texts (only the ones that differ from the default).</summary>
        public static Dictionary<string, string> Overrides(LeadFormSetting? settings)
        {
            if (string.IsNullOrWhiteSpace(settings?.AiMessagesJson)) return new();
            try { return JsonSerializer.Deserialize<Dictionary<string, string>>(settings.AiMessagesJson!) ?? new(); }
            catch (JsonException) { return new(); }
        }

        /// <summary>The text to send, with {الاسم} / {الموظف} filled in.</summary>
        public static string Get(LeadFormSetting? settings, string key, string? customerName = null, string? agentName = null)
        {
            var text = Overrides(settings).TryGetValue(key, out var edited) && !string.IsNullOrWhiteSpace(edited)
                ? edited
                : ByKey[key].Default;
            var name = string.IsNullOrWhiteSpace(customerName) ? "فندم" : customerName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return text.Replace(NamePlaceholder, name).Replace(AgentPlaceholder, agentName ?? "");
        }

        /// <summary>JSON of the edited texts to store (null when everything is the default).</summary>
        public static string? ToJson(IDictionary<string, string>? posted)
        {
            var kept = new Dictionary<string, string>();
            foreach (var (key, raw) in posted ?? new Dictionary<string, string>())
            {
                if (!ByKey.TryGetValue(key, out var def)) continue;
                var text = raw?.Trim().Replace("\r\n", "\n");
                if (string.IsNullOrEmpty(text) || text == def.Default) continue;
                kept[key] = text.Length > 1000 ? text[..1000] : text;
            }
            return kept.Count == 0 ? null : JsonSerializer.Serialize(kept);
        }
    }
}
