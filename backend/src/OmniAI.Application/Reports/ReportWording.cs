using System.Collections.Frozen;
using System.Globalization;
using OmniAI.Application.Customers;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Reports;

/// <summary>
/// Every word a report file says that the platform wrote, in English and Arabic
/// (Phase 37.7, D-6, plan row 36).
/// </summary>
/// <remarks>
/// <para>
/// <strong>English is the key, and is returned untouched.</strong> The builder
/// and the renderers ask for an English phrase; in English they get exactly that
/// phrase back, so an English report is the file it was before 37.7 (proved
/// against golden files). In Arabic they get the phrase's Arabic, written to the
/// glossary in <c>frontend/src/locale/GLOSSARY.md</c> and to the words the
/// Arabic screens already use.
/// </para>
/// <para>
/// <strong>Only the platform's own words change.</strong> Titles, labels, column
/// headings, sheet names, notes, and the fixed values the platform writes into
/// cells (channel names, outcomes, delivery states, customer labels for people
/// nobody named). Data stays as it was stored: a customer's or an agent's name,
/// a model, a provider, a currency code, an id, every number and every date.
/// </para>
/// <para>
/// <strong>Digits and dates are the same in both languages</strong> (localisation
/// decision 3): Western digits, the Gregorian calendar, ISO dates. Nothing here
/// formats a number.
/// </para>
/// <para>
/// <strong>A missing Arabic phrase falls back to English</strong> rather than
/// failing a report somebody is waiting for. Tests build every report type in
/// Arabic and fail on any heading, label or note without Arabic, so a fallback
/// never ships unnoticed.
/// </para>
/// </remarks>
public sealed class ReportWording
{
    public static readonly ReportWording English = new(UserLanguage.English);

    public static readonly ReportWording Arabic = new(UserLanguage.Arabic);

    /// <summary>UTC in an Arabic date or period label: one form everywhere (glossary, 36.11).</summary>
    private const string ArabicUtc = "بالتوقيت العالمي";

    /// <summary>
    /// LEFT-TO-RIGHT MARK (U+200E), written around an ISO date inside an Arabic
    /// sentence.
    /// </summary>
    /// <remarks>
    /// Without it the bidirectional algorithm turns digits that follow Arabic
    /// letters into Arabic numbers (UAX #9, rule W2), the hyphens then separate
    /// them, and <c>2026-10-01</c> is shown as <c>01-10-2026</c>, in the PDF, in
    /// Excel and in a browser alike. The marks are invisible and keep the date in
    /// its own left-to-right order wherever the sentence is shown.
    /// </remarks>
    private const char LeftToRightMark = (char)0x200E;

    private static readonly FrozenDictionary<string, string> ArabicTerms = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // ------------------------------------------------- shared labels
        ["Conversations started"] = "المحادثات التي بدأت",
        ["Customer messages"] = "رسائل العملاء",
        ["AI messages"] = "رسائل الذكاء الاصطناعي",
        ["Human messages"] = "رسائل الفريق",
        ["System messages"] = "رسائل النظام",
        ["AI replies"] = "ردود الذكاء الاصطناعي",
        ["Human replies"] = "ردود الفريق",
        ["Open conversations"] = "المحادثات المفتوحة",
        ["Counts and safe labels only. This report contains no message text."] =
            "أعداد وتسميات آمنة فقط. لا يتضمن هذا التقرير نص أي رسالة.",

        // ---------------------------------------- conversation activity
        ["Open conversations (current snapshot)"] = "المحادثات المفتوحة (لحظة إنشاء التقرير)",
        ["Open, handled by AI (current snapshot)"] = "مفتوحة، يردّ عليها الذكاء الاصطناعي (لحظة إنشاء التقرير)",
        ["Open, handled by a person (current snapshot)"] = "مفتوحة، يردّ عليها أحد أعضاء الفريق (لحظة إنشاء التقرير)",
        ["Daily activity"] = "النشاط اليومي",
        ["Snapshot figures describe the workspace at the moment the report was generated, not the period."] =
            "تصف أرقام «لحظة إنشاء التقرير» حال مساحة العمل عند إنشاء التقرير، لا خلال الفترة.",

        // ------------------------------------------------ channel usage
        ["Channels"] = "القنوات",
        ["Activity by channel"] = "النشاط بحسب القناة",
        ["Channel"] = "القناة",
        ["A message is attributed to the channel of the conversation it belongs to."] =
            "تُنسب كل رسالة إلى قناة المحادثة التي تنتمي إليها.",

        // ------------------------------------------- AI and human handling
        ["AI turns attempted"] = "محاولات ردّ الذكاء الاصطناعي",
        ["AI turns completed"] = "محاولات الردّ التي اكتملت",
        ["AI turns failed"] = "محاولات الردّ التي فشلت",
        ["AI turns still in progress"] = "محاولات الردّ الجارية",
        ["Daily replies"] = "الردود اليومية",
        ["Replies by day"] = "الردود بحسب اليوم",
        ["Current handling"] = "من يردّ الآن",
        ["Open conversations by handling (current snapshot)"] =
            "المحادثات المفتوحة بحسب من يردّ عليها (لحظة إنشاء التقرير)",
        ["Handled by"] = "يردّ عليها",
        ["AI"] = "الذكاء الاصطناعي",
        ["A person"] = "أحد أعضاء الفريق",
        ["Open-conversation figures are a snapshot at the moment the report was generated, not the period."] =
            "أعداد المحادثات المفتوحة هي حالها لحظة إنشاء التقرير، لا خلال الفترة.",
        ["An AI turn is one attempt to answer a customer; it is counted when it started."] =
            "محاولة الردّ هي محاولة واحدة من الذكاء الاصطناعي للإجابة عن عميل، وتُحتسب عند بدئها.",

        // --------------------------------------------- customer activity
        ["Active customers"] = "العملاء النشطون",
        ["Customers"] = "العملاء",
        ["Customer ID"] = "معرّف العميل",
        ["Customer"] = "العميل",
        ["Conversations active"] = "المحادثات النشطة",
        ["First activity (UTC)"] = "أول نشاط (" + ArabicUtc + ")",
        ["Last activity (UTC)"] = "آخر نشاط (" + ArabicUtc + ")",
        ["A customer is active if a message was sent or a conversation was started in one of their conversations during the period."] =
            "يُعدّ العميل نشطًا إذا أُرسلت رسالة أو بدأت محادثة في إحدى محادثاته خلال الفترة.",
        ["Customers are canonical: records combined by a merge appear once."] =
            "يظهر كل عميل مرة واحدة: السجلات التي دُمجت تُعدّ عميلًا واحدًا.",
        ["No phone number, WhatsApp identifier, email address or chat credential is included."] =
            "لا يتضمن التقرير أي رقم هاتف أو معرّف واتساب أو عنوان بريد إلكتروني أو بيانات دخول إلى الدردشة.",

        // ------------------------------------------------ agent activity
        ["AI agents with activity"] = "وكلاء الذكاء الاصطناعي الذين لهم نشاط",
        ["AI agents"] = "وكلاء الذكاء الاصطناعي",
        ["Activity by AI agent"] = "النشاط بحسب وكيل الذكاء الاصطناعي",
        ["AI agent ID"] = "معرّف الوكيل",
        ["AI agent"] = "وكيل الذكاء الاصطناعي",
        ["Human replies in its conversations"] = "ردود الفريق في محادثاته",
        ["Ordered by conversations started, then customer messages, then name, then id."] =
            "مرتّبون بحسب المحادثات التي بدأت، ثم رسائل العملاء، ثم الاسم، ثم المعرّف.",
        ["Human replies are counted in the conversations an agent was handling; they are not work the agent did."] =
            "تُحتسب ردود الفريق في المحادثات التي كان الوكيل يتولّاها، وهي ليست عملًا قام به الوكيل.",

        // ------------------------------------------------ provider usage
        ["Provider calls"] = "استدعاءات المزوّدين",
        ["Text generation calls"] = "استدعاءات توليد النصوص",
        ["Embedding calls"] = "استدعاءات التضمين",
        ["Generation input tokens"] = "رموز الإدخال في التوليد",
        ["Generation cached input tokens"] = "رموز الإدخال المخزّنة مؤقتًا في التوليد",
        ["Generation output tokens"] = "رموز الإخراج في التوليد",
        ["Generation reasoning tokens"] = "رموز الاستدلال في التوليد",
        ["Embedding input tokens"] = "رموز الإدخال في التضمين",
        ["Models"] = "النماذج",
        ["Calls by provider, model and operation"] = "الاستدعاءات بحسب المزوّد والنموذج والعملية",
        ["Provider"] = "المزوّد",
        ["Model"] = "النموذج",
        ["Operation"] = "العملية",
        ["Calls"] = "الاستدعاءات",
        ["Input tokens"] = "رموز الإدخال",
        ["Output tokens"] = "رموز الإخراج",
        ["Text generation"] = "توليد النصوص",
        ["Embedding"] = "التضمين",
        ["Outcomes"] = "النتائج",
        ["Call outcomes"] = "نتائج الاستدعاءات",
        ["Outcome"] = "النتيجة",
        ["Succeeded"] = "نجحت",
        ["Failed"] = "فشلت",
        ["Unknown (dispatched, outcome not established)"] = "غير معروفة (أُرسلت ولم تُعرف نتيجتها)",
        ["Calls are real provider requests, counted when they were dispatched."] =
            "الاستدعاءات طلبات فعلية إلى المزوّدين، وتُحتسب عند إرسالها.",
        ["Tokens are the providers' own reported figures; a call whose usage was not reported adds nothing."] =
            "الرموز هي الأرقام التي أبلغ عنها المزوّدون أنفسهم، والاستدعاء الذي لم يُبلَّغ عن استخدامه لا يضيف شيئًا.",

        // ------------------------------------------------- provider cost
        ["Currencies"] = "العملات",
        ["Unrated calls"] = "الاستدعاءات غير المسعَّرة",
        ["Estimated provider cost by model"] = "التكلفة التقديرية للمزوّدين بحسب النموذج",
        ["Estimated provider cost"] = "التكلفة التقديرية للمزوّدين",
        ["Currency"] = "العملة",
        ["By currency"] = "بحسب العملة",
        ["Estimated provider cost by currency"] = "التكلفة التقديرية للمزوّدين بحسب العملة",
        ["Rated calls"] = "الاستدعاءات المسعَّرة",
        ["Estimated provider cost is internal operational accounting of what AI providers were used for. "
            + "It is not billing, and nothing in it is charged to anyone."] =
            "التكلفة التقديرية للمزوّدين محاسبة تشغيلية داخلية لما استُخدم فيه مزوّدو الذكاء الاصطناعي. "
            + "وهي ليست فوترة، ولا يُحمَّل أي مبلغ منها على أحد.",
        ["Currencies are never added together and nothing is converted: each currency has its own total."] =
            "لا تُجمع العملات معًا ولا يُحوَّل أي مبلغ: لكل عملة مجموعها الخاص.",
        ["A model with no cost shown was either not rated, or rated in more than one currency."] =
            "النموذج الذي لا تظهر له تكلفة إمّا لم يُسعَّر، وإمّا سُعِّر بأكثر من عملة.",
        ["Unrated calls succeeded but had no configured rate. They are real usage, not free."] =
            "الاستدعاءات غير المسعَّرة نجحت لكن لم يُضبط لها سعر. وهي استخدام فعلي، وليست مجانية.",

        // --------------------------------------------- WhatsApp delivery
        ["Outbound WhatsApp messages"] = "رسائل واتساب الصادرة",
        ["Delivery status"] = "حالة التسليم",
        ["Outbound messages by delivery status"] = "الرسائل الصادرة بحسب حالة التسليم",
        ["Status"] = "الحالة",
        ["Messages"] = "الرسائل",
        ["Pending"] = "في الانتظار",
        ["Sending"] = "جارٍ الإرسال",
        ["Sent"] = "أُرسلت",
        ["Delivered"] = "وصلت",
        ["Read"] = "قُرئت",
        ["Unknown"] = "غير معروفة",
        ["Messages are counted by when they were queued, at the status they have now."] =
            "تُحتسب الرسائل بحسب وقت إدراجها للإرسال، وبالحالة التي هي عليها الآن.",
        ["Unknown means a send was interrupted and it cannot be established whether it arrived."] =
            "تعني «غير معروفة» أن الإرسال انقطع ولا يمكن التأكد من وصول الرسالة.",

        // ----------------------------------------------- channel names
        ["Website"] = "دردشة الموقع",
        ["WhatsApp"] = "واتساب",
        ["Instagram"] = "إنستجرام",
        ["Messenger"] = "ماسنجر",

        // ------------------------------------------------- the file itself
        ["Workspace"] = "مساحة العمل",
        ["Generated (UTC)"] = "وقت الإنشاء (" + ArabicUtc + ")",
        ["Summary"] = "الملخص",
        ["Notes"] = "ملاحظات",
        ["No rows for this period."] = "لا توجد صفوف لهذه الفترة.",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private ReportWording(UserLanguage language)
    {
        Language = language;
    }

    public UserLanguage Language { get; }

    /// <summary>The product's public name in this language: Nabra, or the Arabic name (brand.md).</summary>
    public string Product => EmailWords.Product(Language);

    /// <summary>Whether the file reads right to left: an Arabic report.</summary>
    public bool IsRightToLeft => Language == UserLanguage.Arabic;

    /// <summary>Every English phrase that has an Arabic one, for completeness tests.</summary>
    public static IReadOnlyCollection<string> TranslatedPhrases => ArabicTerms.Keys;

    /// <summary>A fixed phrase, in this wording's language.</summary>
    public string this[string english]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(english);

            return IsRightToLeft && ArabicTerms.TryGetValue(english, out string? arabic) ? arabic : english;
        }
    }

    public static ReportWording For(UserLanguage language) =>
        language == UserLanguage.Arabic ? Arabic : English;

    /// <summary>The report's name: the catalogue's title, or the Arabic name its emails use (37.3).</summary>
    public string Title(ReportType type) => NotificationCatalog.ReportName(type, Language);

    /// <summary>The day column, naming the zone its dates are local dates of (Phase 37.6).</summary>
    public string DateHeader(WorkspaceTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return IsRightToLeft
            ? "التاريخ (" + ArabicZone(zone) + ")"
            : string.Create(CultureInfo.InvariantCulture, $"Date ({zone})");
    }

    /// <summary>The first note of every report: the days it covers, and whose days they are.</summary>
    public string PeriodNote(ReportPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        return IsRightToLeft
            ? $"الفترة: من {IsolatedDate(period.FromDate)} إلى {IsolatedDate(period.LastDate)} شاملةً، بأيام كاملة {ArabicZone(period.TimeZone)}."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Period: {period.FromDate:yyyy-MM-dd} to {period.LastDate:yyyy-MM-dd} inclusive, in {period.TimeZone} whole days.");
    }

    /// <summary>The PDF's period label.</summary>
    public string PeriodLabel(WorkspaceTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return IsRightToLeft
            ? "الفترة (" + ArabicZone(zone) + ")"
            : string.Create(CultureInfo.InvariantCulture, $"Period ({zone})");
    }

    /// <summary>The PDF's period value: two ISO dates, the last one included.</summary>
    public string PeriodRange(ReportPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        return IsRightToLeft
            ? $"من {IsolatedDate(period.FromDate)} إلى {IsolatedDate(period.LastDate)} شاملةً"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{period.FromDate:yyyy-MM-dd} to {period.LastDate:yyyy-MM-dd} inclusive");
    }

    /// <summary>The workbook's period-start label.</summary>
    public string PeriodStart(WorkspaceTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return IsRightToLeft ? "بداية الفترة (" + ArabicZone(zone) + ")" : $"Period start ({zone})";
    }

    /// <summary>The workbook's period-end label.</summary>
    public string PeriodEnd(WorkspaceTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return IsRightToLeft ? "نهاية الفترة (" + ArabicZone(zone) + "، ضمنها)" : $"Period end ({zone}, inclusive)";
    }

    /// <summary>The PDF footer's page number.</summary>
    public string PageOf(int number, int total) =>
        IsRightToLeft
            ? string.Create(CultureInfo.InvariantCulture, $"صفحة {number} من {total}")
            : string.Create(CultureInfo.InvariantCulture, $"Page {number} of {total}");

    public string Channel(ChannelKind channel) => this[channel switch
    {
        ChannelKind.WebChat => "Website",
        _ => channel.ToString(),
    }];

    public string Operation(AiOperationType operation) => this[operation switch
    {
        AiOperationType.TextGeneration => "Text generation",
        AiOperationType.Embedding => "Embedding",
        _ => operation.ToString(),
    }];

    /// <summary>
    /// The name of the provider a call went to, as a reader knows it, rather than
    /// the code the usage ledger stores (Phase 39.8a): "OpenAI" for
    /// <see cref="AiProvider.OpenAi"/>, and "Local test stand-in" for the
    /// Development-only <see cref="AiProvider.DevelopmentStandIn"/> (Phase 37.2),
    /// whose replies already say "Local test reply". A code this file does not
    /// know is printed as it is stored, so nothing is ever hidden.
    /// </summary>
    /// <remarks>
    /// OpenAI is the company's own name and stays in Latin script in an Arabic
    /// file too, like WhatsApp's model names; the stand-in is a description, so
    /// it is translated.
    /// </remarks>
    public string Provider(string code) => code switch
    {
        nameof(AiProvider.OpenAi) => "OpenAI",
        nameof(AiProvider.DevelopmentStandIn) => IsRightToLeft ? "البديل التجريبي المحلي" : "Local test stand-in",
        _ => code,
    };

    /// <summary>
    /// Channel names joined into one cell: <c>Website, WhatsApp</c> or
    /// <c>دردشة الموقع، واتساب</c>.
    /// </summary>
    public string Channels(IEnumerable<ChannelKind> channels) =>
        string.Join(IsRightToLeft ? "، " : ", ", channels.Select(Channel));

    /// <summary>
    /// What a customer is called: their name as stored, or the label for somebody
    /// nobody named, by the same rule the screens use (<see cref="CustomerLabels"/>).
    /// </summary>
    public string Customer(CustomerDisplayName? displayName, IReadOnlyList<ChannelKind> channels)
    {
        if (!IsRightToLeft)
        {
            return CustomerLabels.For(displayName, channels);
        }

        return CustomerLabels.CodeFor(displayName, channels) switch
        {
            CustomerLabelCode.Named => displayName!.Value,
            CustomerLabelCode.UnnamedCustomer => "عميل بلا اسم",
            CustomerLabelCode.WhatsAppCustomer => "عميل واتساب",
            _ => "زائر مجهول",
        };
    }

    /// <summary>An ISO date that keeps its order inside an Arabic sentence.</summary>
    internal static string IsolatedDate(DateOnly date) =>
        LeftToRightMark + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + LeftToRightMark;

    /// <summary>
    /// A zone in an Arabic label: "بالتوقيت العالمي" for UTC, else
    /// "بتوقيت" and the IANA name as it is spelt.
    /// </summary>
    private static string ArabicZone(WorkspaceTimeZone zone) =>
        zone == WorkspaceTimeZone.Utc ? ArabicUtc : "بتوقيت " + zone.Name;
}
