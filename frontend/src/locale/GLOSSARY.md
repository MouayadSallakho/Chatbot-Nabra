# Arabic glossary

The terms Nabra uses, so the same thing is called the same thing on every screen. Approved in
Phase 36.3a (`docs/architecture/localisation-arabic-rtl.md`, section 6); a native reviewer refines
it before each page phase ships.

`npm run i18n:check` warns when an English message contains a term from the **Checked** rows and
its Arabic translation does not contain the **Match** text (any of them, when several are separated by `/`). Match is the stem without the article
(ال), so the definite and indefinite forms both pass. The check is a reminder, not a gate: a
sentence can have a good reason to word it differently.

Arabic copy uses Arabic punctuation (، ؛ ؟) and «» for quoted names. Status adjectives agree with
their noun (an agent is مفعّل, a channel is مفعّلة), so they are separate messages per entity.

| English              | Arabic                       | Match                 | Checked | Note                                                     |
| -------------------- | ---------------------------- | --------------------- | ------- | -------------------------------------------------------- |
| Overview             | نظرة عامة                    | نظرة عامة             | yes     |                                                          |
| Conversations        | المحادثات                    | محادث                 | yes     | singular محادثة                                          |
| Inbox                | صندوق المحادثات              | صندوق المحادثات       | yes     | not صندوق الوارد, which reads as e-mail                  |
| Customers            | العملاء                      | عمل                   | no      | singular عميل; counts per plural category                |
| AI Agents            | وكلاء الذكاء الاصطناعي       | وكيل/وكلاء/وكيلَي     | yes     | singular وكيل ذكاء اصطناعي; "agent" alone: وكيل          |
| Knowledge            | المعرفة                      | معرفة                 | yes     | page title قاعدة المعرفة                                 |
| Channels             | القنوات                      | قناة/قنوات            | yes     | singular قناة (feminine)                                 |
| Website chat         | دردشة الموقع                 | دردشة الموقع          | yes     |                                                          |
| Analytics            | التحليلات                    | تحليلات               | yes     |                                                          |
| Reports              | التقارير                     | تقارير                | no      | singular تقرير                                           |
| Team                 | الفريق                       | فريق                  | yes     |                                                          |
| Roles & permissions  | الأدوار والصلاحيات           | الأدوار والصلاحيات    | yes     |                                                          |
| Plan & usage         | الباقة والاستخدام            | الباقة والاستخدام     | yes     |                                                          |
| Billing              | الفوترة                      | فوترة                 | yes     |                                                          |
| Notifications        | الإشعارات                    | إشعار                 | yes     |                                                          |
| Workspace            | مساحة العمل                  | مساحة العمل/مساحة عمل | yes     | indefinite مساحة عمل («مساحة عمل أخرى»)                  |
| Take over            | استلام المحادثة              | استلام                | yes     | button, verbal noun                                      |
| Return to AI         | إعادتها إلى الذكاء الاصطناعي | إعادت                 | yes     | button; ها = the conversation                            |
| Handled by AI        | يردّ الذكاء الاصطناعي        | يردّ                  | no      |                                                          |
| Handled by a person  | يردّ أحد أعضاء الفريق        | يردّ                  | no      |                                                          |
| AI (voice)           | الذكاء الاصطناعي             |                       | no      | the Lagoon voice                                         |
| Person (voice)       | الفريق                       |                       | no      | the Ember voice; gender-neutral                          |
| Draft                | مسودة                        | مسودة                 | no      |                                                          |
| Active               | مفعّل                        |                       | no      | agrees with its noun: مفعّلة for a channel               |
| Disabled             | معطّل                        |                       | no      | agrees with its noun: معطّلة for a channel               |
| Sign in              | تسجيل الدخول                 | الدخول                | yes     |                                                          |
| Sign out             | تسجيل الخروج                 | الخروج                | yes     |                                                          |
| Member               | عضو                          |                       | no      | plural أعضاء                                             |
| Invitation           | دعوة                         |                       | no      |                                                          |
| Role                 | دور                          |                       | no      |                                                          |
| Permission           | صلاحية                       |                       | no      |                                                          |
| Handoff              | التحويل إلى الفريق           |                       | no      |                                                          |
| Transcript           | نص المحادثة                  | نص المحادثة           | no      |                                                          |
| Document (knowledge) | مستند                        |                       | no      |                                                          |
| Indexing             | الفهرسة                      | فهرس                  | yes     |                                                          |
| Widget               | نافذة الدردشة                |                       | no      | the embedded chat                                        |
| Hosted chat page     | صفحة الدردشة المستضافة       |                       | no      |                                                          |
| Allowed websites     | المواقع المسموح بها          |                       | no      |                                                          |
| Usage limit          | حد الاستخدام                 |                       | no      |                                                          |
| Subscription         | الاشتراك                     |                       | no      |                                                          |
| Invoice              | فاتورة                       |                       | no      |                                                          |
| Grace period         | مهلة السماح                  |                       | no      | while a payment has failed (Phase 36.10)                 |
| Fallback plan        | الباقة البديلة               |                       | no      | the plan a lapsed workspace lands on                     |
| Plan                 | الباقة                       |                       | no      | «الباقة السارية الآن» for the plan in effect             |
| Export               | تصدير                        |                       | no      |                                                          |
| Schedule             | جدولة                        |                       | no      |                                                          |
| Save changes         | حفظ التغييرات                | حفظ التغييرات         | yes     |                                                          |
| Cancel               | إلغاء                        | إلغاء                 | no      |                                                          |
| Retry                | إعادة المحاولة               |                       | no      |                                                          |
| Previous / Next      | السابق / التالي              |                       | no      |                                                          |
| Loading…             | جارٍ التحميل…                |                       | no      |                                                          |
| Page not found       | الصفحة غير موجودة            | غير موجودة            | yes     |                                                          |
| Nothing was lost     | لم يُفقد شيء                 |                       | no      |                                                          |
| Theme                | المظهر                       | مظهر                  | no      | Light فاتح, Dark داكن, System حسب النظام                 |
| Language             | اللغة                        |                       | no      |                                                          |
| WhatsApp             | واتساب                       | واتساب                | yes     | Meta's Arabic form; Instagram إنستجرام, Messenger ماسنجر |
| Nabra                | نبرة                         | نبرة                  | yes     | the product's name; "OmniAI" is a code name, never copy  |
