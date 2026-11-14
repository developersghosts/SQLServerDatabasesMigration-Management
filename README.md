# SQL Server Manager

<div dir="rtl">

برنامج لإدارة قواعد بيانات **SQL Server** وترحيل البيانات بينها، بواجهة **عربي / إنجليزي** ووضع **نهاري / ليلي**. يعمل في المتصفح أو كبرنامج سطح مكتب على ويندوز.

</div>

A tool to **manage SQL Server databases** and **migrate data** between them, with an **Arabic / English** interface and **light / dark** themes. It runs in the browser or as a Windows desktop app.

---

## المحتويات · Contents

**البداية · Getting started**
[المتطلبات · Requirements](#requirements) ·
[التشغيل من الكود · Run from source](#run) ·
[برنامج سطح المكتب · Desktop app](#desktop)

**عام · General**
[1. اللغة · Language](#f1) ·
[2. الوضع النهاري والليلي · Theme](#f2) ·
[3. الأقسام · Modes](#f3)

**إدارة قواعد البيانات · Database Manager**
[4. الاتصال بسيرفر · Connect](#f4) ·
[5. مستكشف الكائنات · Object Explorer](#f5) ·
[6. محرر الاستعلامات · Query editor](#f6) ·
[7. النتائج والرسائل · Results and messages](#f7) ·
[8. تعديل خلية من النتائج · Editing a result cell](#f8) ·
[9. عرض أول 1000 صف وتعديل أول 200 صف · Select / Edit rows](#f9) ·
[10. مصمم الجداول · Table designer](#f10) ·
[11. السكربتات · Scripting](#f11) ·
[12. تنفيذ إجراء مخزن · Execute procedure](#f12) ·
[13. الفهارس · Indexes](#f13) ·
[14. المفاتيح والقيود والمشغلات وباقي الكائنات · Keys, constraints, triggers and other objects](#f14) ·
[15. إنشاء قاعدة بيانات · New database](#f15) ·
[16. النسخ الاحتياطي · Back up](#f16) ·
[17. الاستعادة · Restore](#f17) ·
[18. الإرفاق والفصل · Attach and detach](#f18) ·
[19. مهام قاعدة البيانات الأخرى · Other database tasks](#f19) ·
[20. خصائص قاعدة البيانات · Database properties](#f20) ·
[21. إنشاء السكربتات · Generate Scripts](#f21) ·
[22. الأمان · Security](#f22) ·
[23. مهام SQL Server Agent · Agent jobs](#f23) ·
[24. مراقب النشاط · Activity Monitor](#f24) ·
[25. خصائص السيرفر · Server properties](#f25) ·
[26. مستعرض ملفات السيرفر · Server file browser](#f26) ·
[27. نوافذ التأكيد · Confirmation dialogs](#f27)

**ترحيل البيانات · Data Migration**
[28. الاتصال بالمصدر والوجهة · Connections](#f28) ·
[29. اختيار الجداول · Choosing tables](#f29) ·
[30. المراجعة قبل النسخ · Review](#f30) ·
[31. النسخ والحفظ والتراجع · Copy, save, rollback](#f31) ·
[32. الربط مع إدارة قواعد البيانات · Link with the manager](#f32)

**مرجع · Reference**
[اختصارات لوحة المفاتيح · Keyboard shortcuts](#shortcuts) ·
[ملاحظات · Notes](#notes) ·
[هيكل المشروع · Project structure](#structure)

---

<a id="requirements"></a>
## المتطلبات · Requirements

<div dir="rtl">

- ويندوز 10 أو 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download) للتشغيل من الكود. النسخة المنشورة لا تحتاجه.
- SQL Server 2005 أو أحدث. تعديل خلايا نتائج الاستعلام يحتاج SQL Server 2012 أو أحدث.

</div>

- Windows 10 or 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to run from source. The published app doesn't need it.
- SQL Server 2005 or later. Editing query result cells needs SQL Server 2012 or later.

<a id="run"></a>
## التشغيل من الكود · Run from source

```powershell
dotnet run --launch-profile http
```

<div dir="rtl">

بعد التشغيل افتح في المتصفح:
- <span dir="ltr">http://localhost:5232/manager</span> لإدارة قواعد البيانات.
- <span dir="ltr">http://localhost:5232</span> لترحيل البيانات.

</div>

Then open http://localhost:5232/manager for the database manager, or http://localhost:5232 for data migration.

<a id="desktop"></a>
## برنامج سطح المكتب · Desktop app

```powershell
.\publish-desktop.ps1
```

<div dir="rtl">

السكربت ينشر البرنامج ويثبّته في <span dir="ltr">`%LOCALAPPDATA%\Programs\SQL Server Manager`</span>، ويضيف اختصاراً باسم **SQL Server Manager** على سطح المكتب وفي قائمة ابدأ.

- البرنامج يفتح في نافذته الخاصة بأيقونته، بدون متصفح وبدون شاشة سوداء.
- يشغّل السيرفر الداخلي على منفذ محلي متاح على نفس الجهاز فقط، ويتوقف بالكامل عند إغلاق النافذة.
- النسخة مستقلة (Self-contained)، يعني تعمل على أي جهاز ويندوز بدون تثبيت .NET.
- اللغة والوضع والسيرفرات الأخيرة تُحفظ في ملف المستخدم، وتبقى بعد تحديث البرنامج.
- لتحديث البرنامج بعد أي تعديل في الكود، شغّل السكربت مرة أخرى. سيغلق النسخة المفتوحة ويستبدلها.

</div>

The script publishes the app to `%LOCALAPPDATA%\Programs\SQL Server Manager` and adds a **SQL Server Manager** shortcut to the Desktop and the Start menu.

- The app opens in its own window with its own icon: no browser, no console window.
- Its web server runs on a free port reachable from this machine only, and stops completely when the window is closed.
- The build is self-contained, so it runs on any Windows PC without installing .NET.
- Language, theme and recent servers are kept in the user profile and survive updates.
- To update after changing the code, run the script again. It closes the running copy and replaces it.

| الخيار · Option | الوظيفة · Effect |
|---|---|
| `-Output <path>` | مكان التثبيت · Install folder |
| `-FrameworkDependent` | حجم أصغر، لكن يحتاج .NET 10 على الجهاز · Smaller, but needs .NET 10 installed |
| `-NoShortcuts` | بدون اختصارات · No shortcuts |

<div dir="rtl">

- للنشر بدون تثبيت: <span dir="ltr">`dotnet publish -p:PublishProfile=Desktop`</span>، والناتج في <span dir="ltr">`bin\Desktop`</span>.
- لتشغيل النسخة المنشورة كسيرفر ويب عادي بدون نافذة: أضف <span dir="ltr">`--server`</span>.

</div>

- To publish without installing: `dotnet publish -p:PublishProfile=Desktop` (output in `bin\Desktop`).
- To run the published app as a plain web server without a window, start it with `--server`.

---

# عام · General

<a id="f1"></a>
## 1. اللغة · Language

<div dir="rtl">

زر **English / العربية** أعلى الصفحة يبدّل لغة الواجهة كلها فوراً، بدون إعادة تحميل الصفحة وبدون فقدان أي شيء مفتوح (الاتصالات، التبويبات، الاستعلامات). الاتجاه يتغير تلقائياً: من اليمين لليسار في العربي، ومن اليسار لليمين في الإنجليزي. اللغة المختارة تُحفظ للمرة القادمة.

الأكواد ونتائج الاستعلامات تبقى من اليسار لليمين دائماً، وكل قيمة نصية تأخذ اتجاهها حسب لغتها (العربي من اليمين والإنجليزي من اليسار).

</div>

The **English / العربية** button at the top switches the whole interface instantly, without reloading the page and without losing anything that is open (connections, tabs, queries). The layout direction follows the language: right-to-left for Arabic, left-to-right for English. The choice is remembered.

Code and result grids always stay left-to-right, and each text value takes the direction of its own language (Arabic right-to-left, English left-to-right).

<a id="f2"></a>
## 2. الوضع النهاري والليلي · Theme

<div dir="rtl">

زر **الشمس / القمر** بجانب زر اللغة يبدّل بين الوضع النهاري والليلي. في البداية يتبع البرنامج إعداد ويندوز، وبعد أول اختيار يُحفظ الوضع ويُطبّق فور فتح الصفحة بدون وميض.

</div>

The **sun / moon** button next to the language button switches between light and dark mode. At first the app follows the Windows setting. After you pick one, the choice is saved and applied as soon as the page opens, without a flash.

<a id="f3"></a>
## 3. الأقسام · Modes

<div dir="rtl">

شريط أعلى الصفحة فيه قسمان:
- **إدارة قواعد البيانات:** كل أدوات الإدارة الشبيهة بـ SQL Server Management Studio.
- **ترحيل البيانات:** نسخ بيانات الجداول من قاعدة إلى أخرى.

التنقل بين القسمين لا يغلق الاتصالات ولا التبويبات المفتوحة.

</div>

The bar at the top has two modes:
- **Database Manager:** the SQL Server Management Studio-style tools.
- **Data Migration:** copying table data from one database to another.

Switching between them keeps your connections and open tabs.

---

# إدارة قواعد البيانات · Database Manager

<a id="f4"></a>
## 4. الاتصال بسيرفر · Connect to a server

<div dir="rtl">

تظهر نافذة الاتصال تلقائياً عند فتح القسم أول مرة، أو من زر **القابس** أعلى مستكشف الكائنات. يمكن الاتصال بأكثر من سيرفر في نفس الوقت.

**طريقتان للاتصال:**
- **الخصائص:** تكتب اسم السيرفر وتختار المصادقة.
- **نص الاتصال (Connection string):** تلصق نص اتصال جاهزاً، ويُقبل أيضاً نص OLE DB الذي يبدأ بـ `Provider=`.

**الحقول:**
- **اسم السيرفر:** مثل `localhost` أو `.\SQLEXPRESS` أو `(localdb)\MSSQLLocalDB` أو `10.0.0.5,1433`. القائمة تقترح السيرفرات المثبتة على الجهاز وآخر السيرفرات التي اتصلت بها.
- **المصادقة:**
  - **مصادقة ويندوز:** تستخدم حساب ويندوز الذي يعمل به البرنامج. تعمل مع SQL Server المثبت على ويندوز (LocalDB أو Express أو سيرفر داخل نفس الدومين).
  - **مصادقة SQL Server:** اسم دخول وكلمة مرور مثل `sa`.
- **قاعدة البيانات:** اختيارية. إذا تركتها فارغة يتصل البرنامج بالقاعدة الافتراضية. وإذا كتبت اسم قاعدة غير موجودة على السيرفر، يتصل بالسيرفر نفسه ويعرض تنبيهاً بأنه تجاهلها.
- **التشفير** و**الوثوق بشهادة السيرفر:** الإعداد الافتراضي (Optional مع الوثوق بالشهادة) يسمح بالاتصال بالإصدارات القديمة والسيرفرات بدون شهادة موثوقة.
- **مهلة الاتصال:** بالثواني.

**مميزات إضافية:**
- اختيار سيرفر محفوظ يعيد طريقة الدخول واسم المستخدم الخاصين به. كلمات المرور لا تُحفظ أبداً.
- إذا كانت اتصالات الترحيل معبأة، يظهر زر لاستخدام اتصال المصدر أو الوجهة مباشرة.
- عند فشل الاتصال تظهر رسالة SQL Server ومعها شرح للسبب المحتمل: كلمة مرور خاطئة، أو حساب ويندوز بدون صلاحية، أو سيرفر Docker/لينكس لا يدعم مصادقة ويندوز، أو سيرفر لا يمكن الوصول إليه.

> **SQL Server على Docker أو لينكس لا يدعم مصادقة ويندوز.** استخدم مصادقة SQL Server، مثلاً السيرفر `localhost,1433` والمستخدم `sa`.

</div>

The connect dialog opens automatically the first time, or from the **plug** button above the Object Explorer. Several servers can be connected at once.

**Two ways to connect:**
- **Properties:** type the server name and choose the authentication.
- **Connection string:** paste a ready connection string. OLE DB strings that start with `Provider=` are accepted too.

**Fields:**
- **Server name:** e.g. `localhost`, `.\SQLEXPRESS`, `(localdb)\MSSQLLocalDB`, `10.0.0.5,1433`. The list suggests SQL Server instances installed on this machine and recently used servers.
- **Authentication:**
  - **Windows Authentication** uses the Windows account the app runs under. It works with SQL Server on Windows (LocalDB, Express, or a server in the same domain).
  - **SQL Server Authentication** uses a login and password, e.g. `sa`.
- **Database:** optional. Empty means the login's default database. If the database doesn't exist on the server, the app connects to the server anyway and tells you it skipped it.
- **Encryption** and **Trust server certificate:** the defaults (Optional, trusted) let you connect to older versions and servers without a trusted certificate.
- **Connection timeout** in seconds.

**Also:**
- Picking a saved server restores its authentication mode and login. Passwords are never saved.
- If the migration connections are filled in, buttons let you reuse the source or destination connection.
- When the connection fails, SQL Server's message is shown with a plain explanation: wrong password, Windows account without access, Docker / Linux server without Windows Authentication, or an unreachable server.

> **SQL Server in Docker or on Linux doesn't support Windows Authentication.** Use SQL Server Authentication, e.g. server `localhost,1433`, login `sa`.

<a id="f5"></a>
## 5. مستكشف الكائنات · Object Explorer

<div dir="rtl">

شجرة على جانب الصفحة تعرض كل محتويات السيرفر، وتُحمّل كل مجموعة عند فتحها أول مرة:

- **السيرفر:** قواعد البيانات (مع مجلد قواعد بيانات النظام)، والأمان (تسجيلات الدخول وأدوار السيرفر)، وSQL Server Agent (المهام المجدولة، غير موجود في Express).
- **قاعدة البيانات:** الجداول (مع عدد الصفوف)، وطرق العرض، والمرادفات، والبرمجة (الإجراءات المخزنة، الدوال، التسلسلات، الأنواع المعرفة)، والأمان (المستخدمون، الأدوار، السكيمات).
- **الجدول:** الأعمدة (مع النوع وPK/FK وNULL)، والمفاتيح، والقيود، والمشغلات، والفهارس.
- **الإجراءات والدوال:** المعاملات وأنواعها.
- قواعد البيانات المتوقفة أو قيد الاستعادة تظهر بحالتها (مثلاً OFFLINE أو RESTORING)، والعناصر المعطلة تظهر باهتة.

**طريقة الاستخدام:**
- **الزر الأيمن** على أي عنصر، أو زر **⋯** بجانبه، يعرض كل الإجراءات المتاحة له. الأقسام التالية تشرح هذه الإجراءات.
- **النقر المزدوج** يفتح العنصر أو يغلقه.
- مربع **البحث** أعلى الشجرة يصفّي الجداول والكائنات المفتوحة بالاسم.
- أزرار الشريط العلوي: اتصال، قطع الاتصال، استعلام جديد على العنصر المحدد، تحديث العنصر المحدد.
- من لوحة المفاتيح: `Enter` للفتح والإغلاق، والأسهم للفتح والإغلاق، و`F5` لتحديث العنصر.
- عرض الشجرة قابل للتغيير بسحب الفاصل، ويُحفظ العرض.

</div>

A tree beside the page shows everything on the server. Each group loads the first time you open it:

- **Server:** Databases (with a System Databases folder), Security (logins and server roles), and SQL Server Agent (jobs; not in Express).
- **Database:** Tables (with row counts), Views, Synonyms, Programmability (stored procedures, functions, sequences, user-defined types) and Security (users, roles, schemas).
- **Table:** Columns (with type, PK/FK and nullability), Keys, Constraints, Triggers and Indexes.
- **Procedures and functions:** their parameters and types.
- Offline or restoring databases show their state (e.g. OFFLINE, RESTORING), and disabled objects are dimmed.

**How to use it:**
- **Right-click** any node, or use its **⋯** button, to see everything you can do with it. The sections below explain these commands.
- **Double-click** expands or collapses a node.
- The **filter** box above the tree filters loaded tables and objects by name.
- Toolbar buttons: connect, disconnect, new query on the selected node, refresh the selected node.
- Keyboard: `Enter` and the arrow keys expand and collapse, `F5` refreshes the node.
- Drag the splitter to resize the tree; the width is remembered.

<a id="f6"></a>
## 6. محرر الاستعلامات · Query editor

<div dir="rtl">

يُفتح من **استعلام جديد** (زر يمين على السيرفر أو قاعدة البيانات، أو زر الشريط). كل تبويب استعلام مستقل:

- **تنفيذ** بـ `F5` أو `Ctrl+E`. إذا حددت جزءاً من النص يُنفّذ المحدد فقط.
- **الدفعات بـ `GO`** مثل SSMS: كل جزء بين `GO` و`GO` يُرسل وحده، و`GO 5` ينفذ الدفعة 5 مرات. كلمة GO داخل التعليقات أو النصوص لا تُحتسب.
- **إلغاء** التنفيذ في أي وقت بزر الإلغاء أو `Alt+Break`.
- **فحص الصياغة** (`Ctrl+F5`): يتحقق من الأخطاء بدون تنفيذ أي شيء.
- **قائمة قاعدة البيانات:** تغيّر القاعدة التي يعمل عليها التبويب. وإذا نفّذت `USE` في السكربت تتحدث القائمة تلقائياً.
- **جلسة ثابتة لكل تبويب:** كما في SSMS، يحتفظ كل تبويب باتصاله، فتبقى الجداول المؤقتة (`#temp`) والمتغيرات والمعاملات المفتوحة (`BEGIN TRAN`) بين مرات التنفيذ. عند إغلاق تبويب فيه معاملة لم تُحفظ يسألك البرنامج قبل الإغلاق، والإغلاق يتراجع عنها.
- **أقصى عدد صفوف:** يحدد عدد الصفوف المعروضة لكل نتيجة (من 1,000 إلى 1,000,000). الصفوف الزائدة تُحسب ولا تُعرض.
- **فتح** ملف `.sql` (يدعم ملفات SSMS بترميز UTF-16) و**حفظ** السكربت بـ `Ctrl+S` كملف UTF-8 يفتح العربي فيه صحيحاً في SSMS وNotepad.
- **مساعدات الكتابة:** أرقام الأسطر، `Tab` و`Shift+Tab` لإزاحة الأسطر، `Ctrl+/` للتعليق، والحفاظ على الإزاحة عند `Enter`.
- **حماية من فقدان العمل:** `F5` على الصفحة ينفذ الاستعلام بدلاً من إعادة تحميل الصفحة، والمتصفح يسألك قبل إغلاق الصفحة إذا كانت فيها تبويبات مفتوحة.
- حجم المحرر قابل للتغيير بسحب الفاصل بينه وبين النتائج.

</div>

Open it with **New query** (right-click a server or database, or the toolbar button). Each query tab is independent:

- **Execute** with `F5` or `Ctrl+E`. If text is selected, only the selection runs.
- **`GO` batches** like SSMS: each part between `GO` lines is sent on its own, and `GO 5` runs a batch five times. `GO` inside comments or strings is ignored.
- **Cancel** a running query any time with the Cancel button or `Alt+Break`.
- **Parse** (`Ctrl+F5`) checks the syntax without running anything.
- **Database list:** changes the database the tab works in. A `USE` in the script updates it.
- **One session per tab**, like SSMS: the tab keeps its connection, so temp tables (`#temp`), variables and open transactions (`BEGIN TRAN`) carry over between runs. Closing a tab with an uncommitted transaction asks first, and closing rolls it back.
- **Max rows** sets how many rows each grid keeps (1,000 to 1,000,000). Extra rows are counted but not shown.
- **Open** a `.sql` file (UTF-16 SSMS files included) and **Save** the script with `Ctrl+S` as UTF-8, so Arabic opens correctly in SSMS and Notepad.
- **Editing helpers:** line numbers, `Tab` / `Shift+Tab` to indent, `Ctrl+/` to comment lines, indentation kept on `Enter`.
- **No lost work:** `F5` on the page runs the query instead of reloading the page, and the browser asks before leaving a page with open tabs.
- Drag the splitter between the editor and the results to resize them.

<a id="f7"></a>
## 7. النتائج والرسائل · Results and messages

<div dir="rtl">

**تبويب النتائج:**
- جدول لكل نتيجة يُرجعها الاستعلام، مع عدد الصفوف والأعمدة.
- الجداول الكبيرة (مئات الآلاف من الصفوف) تُعرض بسرعة لأن الصفحة ترسم الصفوف الظاهرة فقط، والصفوف تظهر أثناء التنفيذ.
- قيم NULL مميزة بلون مختلف، والأرقام محاذاة لليمين.
- النقر على خلية يحددها، و`Ctrl+C` ينسخها.
- **نسخ مع العناوين:** ينسخ النتيجة كاملة بصيغة تُلصق مباشرة في Excel.
- **تصدير CSV:** ينزّل النتيجة كملف CSV يفتح العربي فيه صحيحاً في Excel.

**تبويب الرسائل:**
- رسائل SQL Server بنفس صيغة SSMS، مثل `(3 rows affected)` و`Msg 208, Level 16, State 1, Line 5` ونواتج `PRINT`.
- **النقر على رسالة خطأ ينقلك للسطر** الذي فيه الخطأ داخل المحرر، حتى عند تنفيذ جزء محدد من النص.
- عند وجود أخطاء ينتقل البرنامج تلقائياً لتبويب الرسائل.

**شريط الحالة** أسفل التبويب يعرض حالة التنفيذ بلون (أخضر نجح، أحمر أخطاء، أزرق جارٍ)، والسيرفر وإصداره، واسم الدخول ورقم الجلسة (SPID)، وقاعدة البيانات، والوقت المستغرق، وعدد الصفوف.

</div>

**Results tab:**
- One grid per result set, with row and column counts.
- Large results (hundreds of thousands of rows) stay fast because only visible rows are drawn, and rows appear while the query runs.
- NULL values are highlighted and numbers are right-aligned.
- Click a cell to select it; `Ctrl+C` copies it.
- **Copy with headers** copies the whole grid in a format that pastes straight into Excel.
- **Export CSV** downloads the grid as a CSV file that opens correctly in Excel, Arabic included.

**Messages tab:**
- SQL Server messages in SSMS format: `(3 rows affected)`, `Msg 208, Level 16, State 1, Line 5`, `PRINT` output.
- **Click an error to jump to its line** in the editor, even when only a selection was run.
- When there are errors, the Messages tab opens by itself.

The **status bar** shows the result with a color (green success, red errors, blue running), the server and its version, the login and session id (SPID), the database, the elapsed time and the row count.

<a id="f8"></a>
## 8. تعديل خلية من النتائج · Editing a result cell

<div dir="rtl">

**انقر مرتين** على أي خلية في النتائج (أو `Enter` / `F2`) لعرض قيمتها كاملة في نافذة، ويمكنك تعديلها وحفظها مباشرة في الجدول:

- البرنامج يسأل SQL Server عن الجدول والعمود اللذين جاءت منهما القيمة، بدون إعادة تنفيذ الاستعلام، ثم يحفظ بأمر `UPDATE` على الصف المحدد بمفتاحه الأساسي. النافذة تعرض بالضبط أين سيتم الحفظ وبأي عمود يُحدَّد الصف.
- **اتجاه الكتابة تلقائي:** النص العربي يُكتب من اليمين والإنجليزي من اليسار، ويتغير الاتجاه أثناء الكتابة.
- خيار **NULL** يجعل القيمة فارغة (للأعمدة التي تسمح بذلك).
- القيمة غير المناسبة لنوع العمود (مثل نص في عمود رقمي) تظهر لها رسالة خطأ ولا يُحفظ شيء.
- الحفظ يتم في معاملة، وإذا لم يُعثر على الصف أو تطابق أكثر من صف يتم التراجع.

**متى يكون التعديل متاحاً؟** عندما تأتي القيمة من عمود في جدول، ويكون **المفتاح الأساسي** للجدول ضمن أعمدة النتيجة. مثال: نتائج «عرض أول 1000 صف» قابلة للتعديل، أما `SELECT Name FROM T` (بدون Id) أو `COUNT(*)` فللقراءة فقط، وتوضح النافذة السبب. أعمدة Identity والمحسوبة لا تُعدّل.

</div>

**Double-click** any result cell (or press `Enter` / `F2`) to see its full value in a window, where you can also edit it and save it straight to the table:

- The app asks SQL Server which table and column the value comes from, without running the query again, then saves with an `UPDATE` on that row, found by its primary key. The window shows exactly where it saves and which columns identify the row.
- **Typing direction is automatic:** Arabic is written right-to-left and English left-to-right, and it changes as you type.
- The **NULL** option clears the value (for nullable columns).
- A value that doesn't fit the column type (e.g. text in a number column) shows an error and nothing is saved.
- The save runs in a transaction. If the row isn't found or more than one row matches, it is rolled back.

**When can a cell be edited?** When the value comes from a table column and the table's **primary key** is among the result columns. For example, *Select Top 1000 Rows* results are editable, while `SELECT Name FROM T` (no Id) or `COUNT(*)` are read-only, and the window says why. Identity and computed columns can't be edited.

<a id="f9"></a>
## 9. عرض أول 1000 صف وتعديل أول 200 صف · Select Top 1000 / Edit Top 200 Rows

<div dir="rtl">

من الزر الأيمن على أي جدول:

**عرض أول 1000 صف:** يفتح تبويب استعلام فيه `SELECT TOP (1000)` بكل الأعمدة وينفذه مباشرة. يعمل أيضاً مع طرق العرض (Views).

**تعديل أول 200 صف:** شبكة تعديل مباشرة:
- عدّل أي خلية، والصفوف المعدلة تظهر بلون وعلامة ✎.
- **إضافة صف:** يضيف صفاً جديداً. الخانات التي تتركها تأخذ القيمة الافتراضية أو Identity تلقائياً.
- **حذف صف:** يعلّم الصف للحذف، ويمكن التراجع قبل الحفظ.
- اكتب `NULL` في خلية لجعلها NULL. الأعمدة التي يملؤها SQL Server (Identity، المحسوبة، rowversion) للقراءة فقط.
- **حفظ التغييرات** ينفذ كل التعديلات والإضافات والحذف في معاملة واحدة. إذا فشل صف واحد لا يُحفظ أي شيء، ويظهر الخطأ تحت الصف المسبب.
- **تجاهل التغييرات** يعيد تحميل البيانات.
- شريط التصفية يسمح بتغيير عدد الصفوف (`TOP`) وكتابة شرط `WHERE` وترتيب `ORDER BY`.
- الجداول بدون مفتاح أساسي أو فهرس فريد تُطابق صفوفها بكل القيم، ويُعدّل صف واحد فقط من الصفوف المتطابقة تماماً، ويظهر تنبيه بذلك.
- النصوص العربية والإنجليزية في الخلايا تأخذ اتجاهها تلقائياً.

</div>

Right-click any table:

**Select Top 1000 Rows** opens a query tab with `SELECT TOP (1000)` over all columns and runs it. It works for views too.

**Edit Top 200 Rows** opens an editable grid:
- Change any cell; changed rows are highlighted and marked ✎.
- **Add row** adds a new row. Cells you leave untouched get their default value or identity.
- **Delete row** marks the row for deletion; you can undo before saving.
- Type `NULL` in a cell to set it to NULL. Columns filled by SQL Server (identity, computed, rowversion) are read-only.
- **Save changes** applies all updates, inserts and deletes in one transaction. If one row fails nothing is saved, and the error appears under that row.
- **Discard changes** reloads the data.
- The filter bar sets the row count (`TOP`), a `WHERE` condition and an `ORDER BY`.
- Tables without a primary key or unique index match rows on all their values, and only one of identical rows is changed; a notice says so.
- Arabic and English text in cells takes its own direction.

<a id="f10"></a>
## 10. مصمم الجداول · Table designer

<div dir="rtl">

يُفتح من **تصميم** على جدول موجود، أو **جدول جديد** على مجلد الجداول. فيه أربع صفحات:

**الأعمدة:**
- لكل عمود: مفتاح أساسي (PK)، الاسم، نوع البيانات (مع اقتراح الأنواع المتاحة)، الطول (مثل `50` أو `max` أو `18,2`)، السماح بـ NULL، Identity مع قيمة البداية والزيادة، والقيمة الافتراضية (مثل `getdate()` أو `0`).
- يمكنك إضافة أعمدة، وحذفها، وإعادة تسميتها، وتغيير نوعها، وتغيير المفتاح الأساسي.
- **حفظ** يعرض سكربت التغييرات أولاً، ثم ينفذها كلها في معاملة واحدة: إذا فشل أمر لا يتغير شيء. البرنامج يتعامل مع التفاصيل تلقائياً، مثل حذف القيمة الافتراضية قبل تغيير نوع العمود ثم إعادتها، والحفاظ على Collation العمود، واستخدام `sp_rename` لإعادة التسمية.
- **إنشاء سكربت التغييرات** يفتح السكربت في تبويب استعلام بدون تنفيذه.
- خاصية Identity تُضبط للأعمدة الجديدة فقط، لأن تغييرها في عمود موجود يحتاج إعادة إنشاء الجدول.

**الفهارس والمفاتيح:** قائمة بكل الفهارس (PK وUNIQUE وCLUSTERED وغيرها) مع أعمدتها والأعمدة المضمّنة والتصفية، وأزرار سكربت وإعادة بناء وحذف، وزر فهرس جديد.

**العلاقات:** قائمة المفاتيح الخارجية، ونموذج لإضافة مفتاح خارجي جديد: تختار الجدول المرجعي وأزواج الأعمدة وإجراء الحذف والتحديث (`CASCADE`، `SET NULL`...) وهل يتم التحقق من البيانات الموجودة.

**القيود:** قائمة قيود التحقق (CHECK)، ونموذج لإضافة قيد جديد مثل `[Price] >= 0`.

أعلى المصمم يظهر عدد الصفوف وحجم الجدول وتاريخ إنشائه وآخر تعديل.

</div>

Open it with **Design** on a table, or **New table** on the Tables folder. It has four pages:

**Columns:**
- Per column: primary key, name, data type (with suggestions), length (e.g. `50`, `max`, `18,2`), allow nulls, identity with seed and increment, and default value (e.g. `getdate()`, `0`).
- Add, remove, rename and retype columns, and change the primary key.
- **Save** shows the change script first, then runs it in one transaction: if any statement fails, nothing changes. Details are handled for you, such as dropping a default before changing a column's type and adding it back, keeping the column's collation, and using `sp_rename` for renames.
- **Generate change script** opens the script in a query tab without running it.
- Identity can only be set on new columns, because changing it on an existing column means recreating the table.

**Indexes & keys:** every index (PK, UNIQUE, clustered…) with its columns, included columns and filter, with Script, Rebuild and Delete buttons, and a New index button.

**Relationships:** the foreign keys, and a form to add one: referenced table, column pairs, delete / update actions (`CASCADE`, `SET NULL`…) and whether existing data is checked.

**Constraints:** the CHECK constraints, and a form to add one, e.g. `[Price] >= 0`.

The top of the designer shows the table's row count, size, creation and last modified dates.

<a id="f11"></a>
## 11. السكربتات · Scripting

<div dir="rtl">

من الزر الأيمن ← **سكربت للجدول / لطريقة العرض / للإجراء / للدالة / للمشغّل** يفتح السكربت المطلوب في تبويب استعلام جديد:

- **CREATE:** للجدول يشمل الأعمدة والمفتاح الأساسي والقيود والقيم الافتراضية والمفاتيح الخارجية والفهارس. لطرق العرض والإجراءات والدوال والمشغلات يشمل التعريف الكامل مع إعدادات `ANSI_NULLS` و`QUOTED_IDENTIFIER`.
- **ALTER:** للكائنات البرمجية. أمر **تعديل** (Modify) يفتح نفس السكربت مباشرة لتعديله وتنفيذه.
- **DROP** و**DROP و CREATE**.
- **SELECT** و**INSERT** و**UPDATE** و**DELETE:** قوالب جاهزة بأسماء الأعمدة وأنواعها لتكملها.
- **EXECUTE:** للإجراءات المخزنة، قالب بكل المعاملات.

متاح أيضاً للفهارس والمفاتيح والقيود والمرادفات والتسلسلات والأنواع والمستخدمين والأدوار والسكيمات وتسجيلات الدخول.

**قوالب الكائنات الجديدة:** على المجلدات تجد طريقة عرض جديدة، وإجراء مخزن جديد، ودالة جديدة (Scalar أو Inline أو متعددة الجمل)، ومشغّل جديد، ونوع جديد. كل منها يفتح قالباً جاهزاً للتعديل.

</div>

Right-click → **Script table / view / procedure / function / trigger as** opens the script in a new query tab:

- **CREATE:** for tables, the columns, primary key, constraints, defaults, foreign keys and indexes. For views, procedures, functions and triggers, the full definition with its `ANSI_NULLS` and `QUOTED_IDENTIFIER` settings.
- **ALTER** for programmable objects. **Modify** opens the same script ready to edit and run.
- **DROP** and **DROP and CREATE**.
- **SELECT**, **INSERT**, **UPDATE** and **DELETE** templates with column names and types to fill in.
- **EXECUTE** for stored procedures, with every parameter.

Also available for indexes, keys, constraints, synonyms, sequences, types, users, roles, schemas and logins.

**New-object templates** on the folders: new view, new stored procedure, new function (scalar, inline or multi-statement), new trigger and new type, each opening a ready template.

<a id="f12"></a>
## 12. تنفيذ إجراء مخزن · Execute stored procedure

<div dir="rtl">

زر يمين على إجراء مخزن ← **تنفيذ الإجراء المخزن**: نافذة فيها كل معاملات الإجراء مع أنواعها. اكتب القيم (أو اختر NULL)، فيُنشأ سكربت `EXEC` ويُنفذ في تبويب جديد. يعرض السكربت قيم معاملات الإخراج (OUTPUT) وقيمة الإرجاع (Return Value). الأرقام تُمرر كما هي، وباقي القيم كنصوص.

</div>

Right-click a stored procedure → **Execute stored procedure**: a window lists its parameters and types. Enter values (or choose NULL), and an `EXEC` script is built and run in a new tab. The script shows output parameters and the return value. Numbers are passed as typed; other values as text.

<a id="f13"></a>
## 13. الفهارس · Indexes

<div dir="rtl">

- **فهرس جديد** (على جدول أو مجلد الفهارس أو في المصمم): تختار الاسم والنوع (NONCLUSTERED أو CLUSTERED أو COLUMNSTORE)، وفريد أم لا، وأعمدة المفتاح بالترتيب واتجاه كل عمود (ASC أو DESC)، والأعمدة المضمّنة (INCLUDE)، وشرط تصفية (Filtered index)، ومعامل الملء (Fill factor). السكربت يظهر أثناء الاختيار.
- على أي فهرس: **إعادة البناء**، و**إعادة التنظيم**، و**تعطيل** (مع تحذير أن تعطيل الفهرس المجمّع يمنع قراءة الجدول)، و**تفعيل** (للفهرس المعطل)، و**إعادة تسمية**، و**حذف**، وسكربت.
- على مجلد الفهارس: **إعادة بناء الكل** و**إعادة تنظيم الكل** للجدول.

</div>

- **New index** (on a table, the Indexes folder or in the designer): name, type (nonclustered, clustered, columnstore), unique or not, key columns in order with ASC / DESC, included columns, a filter (filtered index) and fill factor. The script updates as you choose.
- On an index: **Rebuild**, **Reorganize**, **Disable** (with a warning that disabling the clustered index makes the table unreadable), **Enable** (on a disabled index), **Rename**, **Delete** and scripting.
- On the Indexes folder: **Rebuild all** and **Reorganize all** for the table.

<a id="f14"></a>
## 14. المفاتيح والقيود والمشغلات وباقي الكائنات · Keys, constraints, triggers and other objects

<div dir="rtl">

- **المفاتيح والقيود:** سكربت، وإعادة تسمية، وحذف. وللمفاتيح الخارجية وقيود التحقق: **تعطيل** و**تفعيل** (مع التحقق من البيانات). على مجلد القيود: **قيد تحقق جديد**.
- **المشغلات:** تعديل، وسكربت، وتعطيل، وتفعيل، وحذف.
- **الأعمدة:** إعادة تسمية وحذف. الحذف يزيل القيمة الافتراضية المرتبطة بالعمود أولاً.
- **الجداول:** إعادة تسمية، و**تفريغ** (`TRUNCATE`، مع خيار `DELETE` عندما يكون الجدول مرتبطاً بمفاتيح خارجية)، وحذف.
- **مرادف جديد**، و**تسلسل جديد**، و**سكيما جديدة:** نوافذ بسيطة لإدخال الاسم والخصائص.
- إعادة التسمية والحذف متاحة لكل أنواع الكائنات.

</div>

- **Keys and constraints:** script, rename, delete. Foreign keys and CHECK constraints can also be **disabled** and **enabled** (re-checking the data). The Constraints folder has **New check constraint**.
- **Triggers:** modify, script, disable, enable, delete.
- **Columns:** rename and delete. Deleting first removes the column's default constraint.
- **Tables:** rename, **Truncate** (`TRUNCATE`, with a `DELETE` option for tables referenced by foreign keys), delete.
- **New synonym**, **New sequence**, **New schema:** small dialogs for the name and settings.
- Rename and delete are available for every kind of object.

<a id="f15"></a>
## 15. إنشاء قاعدة بيانات · New database

<div dir="rtl">

زر يمين على السيرفر أو مجلد قواعد البيانات ← **قاعدة بيانات جديدة**:

- **الاسم**، و**المالك** (من تسجيلات الدخول)، و**ترتيب الحروف** (Collation، مع اقتراحات أولها العربية مثل `Arabic_CI_AS`)، و**نموذج الاسترداد** (FULL أو SIMPLE أو BULK_LOGGED)، و**مستوى التوافق**.
- خيار **تحديد ملفات البيانات والسجل:** مجلد كل ملف (مع زر استعراض مجلدات السيرفر)، والحجم الأولي، ومقدار النمو، والحجم الأقصى. بدون هذا الخيار يستخدم السيرفر إعداداته الافتراضية.
- القاعدة الجديدة تظهر في الشجرة مباشرة بعد الإنشاء.

</div>

Right-click the server or the Databases folder → **New database**:

- **Name**, **owner** (from the logins), **collation** (suggestions start with Arabic ones such as `Arabic_CI_AS`), **recovery model** (FULL, SIMPLE, BULK_LOGGED) and **compatibility level**.
- **Specify the data and log files:** each file's folder (with a server folder browser), initial size, growth and max size. Without it, the server's defaults are used.
- The new database appears in the tree right away.

<a id="f16"></a>
## 16. النسخ الاحتياطي · Back up

<div dir="rtl">

زر يمين على قاعدة بيانات ← المهام ← **نسخ احتياطي**:

- **النوع:** كامل (Full)، أو تفاضلي (Differential)، أو سجل المعاملات (Log، غير متاح في نموذج SIMPLE). مع خيار نسخة مستقلة (Copy-only).
- **مكان الحفظ:** مسار الملف على جهاز السيرفر، ويُقترح تلقائياً في مجلد النسخ الافتراضي باسم فيه التاريخ والوقت. زر **استعراض** لاختيار المجلد.
- **إذا كان الملف موجوداً:** الإلحاق به أو استبداله.
- **الضغط** (حسب السيرفر، أو مضغوط، أو بدون)، و**حساب Checksum**، و**التحقق من النسخة بعد الانتهاء** (`RESTORE VERIFYONLY`).
- **شريط تقدم** يعرض النسبة أثناء النسخ، مع رسائل SQL Server.

</div>

Right-click a database → Tasks → **Back up**:

- **Type:** Full, Differential, or Transaction log (not available in the SIMPLE recovery model), with an optional copy-only backup.
- **Destination:** a file path on the SQL Server machine, suggested in the default backup folder with the date and time in the name. **Browse** picks the folder.
- **If the file exists:** append to it or overwrite it.
- **Compression** (server default, compress, don't compress), **checksum**, and **verify when finished** (`RESTORE VERIFYONLY`).
- A **progress bar** shows the percentage while the backup runs, with SQL Server's messages.

<a id="f17"></a>
## 17. الاستعادة · Restore

<div dir="rtl">

زر يمين على مجلد قواعد البيانات (أو على قاعدة بيانات ← المهام) ← **استعادة قاعدة بيانات**:

1. اختر **ملف النسخة الاحتياطية** (`.bak` أو `.trn`) بالمسار أو بزر الاستعراض، ثم **قراءة النسخة**.
2. تظهر **مجموعات النسخ** في الملف (النوع، القاعدة، السيرفر، التاريخ، الحجم). البرنامج يختار تلقائياً آخر نسخة كاملة وما بعدها من نسخ تفاضلية ونسخ سجل، ويمكنك تعديل الاختيار.
3. **الاستعادة باسم:** اسم القاعدة الناتجة. يمكن استعادتها كنسخة جديدة باسم مختلف.
4. **ملفات القاعدة:** مسار كل ملف بعد الاستعادة، ويُقترح في مجلدات السيرفر الافتراضية باسم القاعدة الجديدة، ويمكن تعديله.
5. **الخيارات:** استبدال القاعدة الموجودة (`WITH REPLACE`)، وإغلاق الاتصالات الحالية، وترك القاعدة في وضع الاستعادة (`NORECOVERY`) لاستعادة نسخ أخرى لاحقاً.

أثناء الاستعادة يظهر شريط تقدم، وتتحدث الشجرة بعد الانتهاء.

</div>

Right-click the Databases folder (or a database → Tasks) → **Restore database**:

1. Choose the **backup file** (`.bak`, `.trn`) by path or with Browse, then **Read backup**.
2. The file's **backup sets** are listed (type, database, server, date, size). The last full backup and the differential and log backups after it are selected for you; you can change the selection.
3. **Restore as:** the resulting database name. You can restore a copy under a different name.
4. **Database files:** where each file goes, suggested in the server's default folders and named after the new database. You can edit them.
5. **Options:** overwrite the existing database (`WITH REPLACE`), close existing connections, and leave the database restoring (`NORECOVERY`) to restore more backups later.

A progress bar shows while it runs, and the tree refreshes when it's done.

<a id="f18"></a>
## 18. الإرفاق والفصل · Attach and detach

<div dir="rtl">

**فصل (Detach)** — زر يمين على قاعدة ← المهام: يزيل القاعدة من السيرفر مع بقاء ملفاتها على القرص لإرفاقها لاحقاً أو نقلها. فيه خياران: إغلاق الاتصالات الحالية، وتحديث الإحصائيات قبل الفصل.

**إرفاق (Attach)** — زر يمين على مجلد قواعد البيانات:
1. اختر ملف البيانات الرئيسي (`.mdf`) ثم **قراءة الملف**.
2. البرنامج يقرأ من الملف اسم القاعدة وباقي ملفاتها (مثل ملف السجل `.ldf`)، ويتوقع أنها في نفس مجلد الـ `.mdf`. يمكنك تعديل المسارات أو إضافة ملفات.
3. **إعادة إنشاء ملف السجل:** إذا كان ملف `.ldf` مفقوداً.

</div>

**Detach** — right-click a database → Tasks: removes it from the server but keeps its files on disk, to attach later or move. Options: close existing connections, update statistics first.

**Attach** — right-click the Databases folder:
1. Choose the primary data file (`.mdf`), then **Read file**.
2. The app reads the database name and its other files (e.g. the `.ldf` log) from the file, and expects them in the same folder as the `.mdf`. You can edit the paths or add files.
3. **Rebuild the log file** if the `.ldf` is missing.

<a id="f19"></a>
## 19. مهام قاعدة البيانات الأخرى · Other database tasks

<div dir="rtl">

من الزر الأيمن على قاعدة بيانات:

- **إعادة تسمية:** مع خيار إغلاق الاتصالات (إعادة التسمية تحتاج وصولاً حصرياً).
- **حذف:** مع خيار إغلاق الاتصالات وخيار حذف سجل النسخ الاحتياطي والاستعادة. القاعدة وملفاتها تُحذف نهائياً، وتُغلق تبويباتها المفتوحة.
- **إيقاف (Offline) / تشغيل (Online).**
- **تقليص:** تقليص القاعدة مع اختيار نسبة المساحة الحرة المتبقية. تقليص ملف معين متاح من صفحة الملفات في الخصائص.
- **فحص السلامة** (`DBCC CHECKDB`): يعرض تقرير SQL Server كاملاً.
- **تحديث الإحصائيات** (`sp_updatestats`).

</div>

Right-click a database:

- **Rename**, with an option to close connections (renaming needs exclusive access).
- **Delete**, with options to close connections and delete the backup / restore history. The database and its files are removed for good, and its open tabs are closed.
- **Take offline / Bring online.**
- **Shrink** the database, choosing how much free space to leave. Shrinking a single file is on the Files page of its properties.
- **Check integrity** (`DBCC CHECKDB`) shows SQL Server's full report.
- **Update statistics** (`sp_updatestats`).

<a id="f20"></a>
## 20. خصائص قاعدة البيانات · Database properties

<div dir="rtl">

زر يمين على قاعدة ← **الخصائص**: تبويب بأربع صفحات:

- **عام:** الحجم، والمساحة المتاحة، وعدد المستخدمين والملفات، والحالة، والمالك، وتاريخ الإنشاء، ونموذج الاسترداد، ومستوى التوافق، والـ Collation، وتاريخ آخر نسخة كاملة وآخر نسخة سجل.
- **الملفات:** كل ملف بحجمه والمستخدم منه ومجموعته ومساره. يمكنك تكبير الحجم، وتغيير النمو التلقائي (بالميجابايت أو بالنسبة) والحجم الأقصى، وتقليص ملف، وإزالة ملف ثانوي، وإضافة ملف بيانات أو سجل جديد.
- **الخيارات:** المالك، والـ Collation، ونموذج الاسترداد، ومستوى التوافق، وتقييد الوصول (MULTI_USER أو SINGLE_USER أو RESTRICTED_USER)، والتحقق من الصفحات، وللقراءة فقط، وAUTO_CLOSE، وAUTO_SHRINK، والإحصائيات التلقائية، وSnapshot Isolation، وREAD_COMMITTED_SNAPSHOT. عند الحفظ تُنفذ التغييرات فقط.
- **استخدام المساحة:** كل جدول بعدد صفوفه والمساحة المحجوزة ومساحة البيانات والفهارس وغير المستخدمة، مع شريط يوضح نسبته من القاعدة.

</div>

Right-click a database → **Properties** opens a tab with four pages:

- **General:** size, free space, users and files, state, owner, creation date, recovery model, compatibility level, collation, and the last full and log backups.
- **Files:** each file's size, used space, filegroup and path. Grow a file, change autogrowth (MB or %) and max size, shrink a file, remove a secondary file, or add a data or log file.
- **Options:** owner, collation, recovery model, compatibility level, restrict access (MULTI_USER, SINGLE_USER, RESTRICTED_USER), page verify, read-only, AUTO_CLOSE, AUTO_SHRINK, automatic statistics, snapshot isolation and READ_COMMITTED_SNAPSHOT. Saving runs only what changed.
- **Disk usage:** each table's rows, reserved, data, index and unused space, with a bar showing its share of the database.

<a id="f21"></a>
## 21. إنشاء السكربتات · Generate Scripts

<div dir="rtl">

زر يمين على قاعدة ← المهام ← **إنشاء سكربتات**: سكربت لإنشاء القاعدة من جديد على سيرفر آخر:

- **النطاق:** القاعدة بالكامل، أو كائنات محددة تختارها من قائمة (مع بحث وتحديد الكل).
- **الكائنات:** الجداول، وطرق العرض، والإجراءات، والدوال، والمشغلات، والمرادفات.
- **تضمين البيانات:** أوامر `INSERT` لبيانات الجداول، مع حد أقصى للصفوف لكل جدول (تدعم Identity).
- **حذف الكائنات قبل إنشائها**، و**البدء بـ `USE`**.
- **الإخراج:** في تبويب استعلام جديد، أو كملف `.sql`.

الترتيب مناسب للتنفيذ: السكيمات، ثم الجداول، ثم البيانات، ثم المفاتيح الخارجية، ثم الكائنات البرمجية حسب ترتيب إنشائها، ثم المشغلات.

</div>

Right-click a database → Tasks → **Generate scripts** builds a script that recreates the database on another server:

- **Scope:** the whole database, or specific objects picked from a list (with filter and select all).
- **Objects:** tables, views, procedures, functions, triggers, synonyms.
- **Script the data too:** `INSERT` statements for table data, with a row limit per table (identity values kept).
- **Drop objects before creating them** and **start with `USE`**.
- **Output:** a new query tab or a `.sql` file.

The order runs cleanly: schemas, tables, data, foreign keys, programmable objects in creation order, then triggers.

<a id="f22"></a>
## 22. الأمان · Security

<div dir="rtl">

**تسجيلات الدخول** (أمان السيرفر ← تسجيلات الدخول):
- **تسجيل دخول جديد / الخصائص:** نافذة بأربع صفحات:
  - **عام:** الاسم، ونوع المصادقة (ويندوز أو SQL)، وكلمة المرور وتأكيدها، وفرض سياسة كلمات المرور وانتهاء الصلاحية، والقاعدة الافتراضية.
  - **أدوار السيرفر:** مثل sysadmin وdbcreator.
  - **ربط المستخدمين:** لكل قاعدة: إنشاء مستخدم لتسجيل الدخول، والسكيما الافتراضية، والأدوار الأساسية (db_owner، db_datareader، db_datawriter، db_ddladmin).
  - **الحالة:** تفعيل أو تعطيل تسجيل الدخول.
- كلمة المرور لا تظهر في معاينة السكربت (تظهر كنجوم).
- تفعيل، وتعطيل، وإعادة تسمية، وحذف.

**المستخدمون** (القاعدة ← الأمان ← المستخدمون): مستخدم جديد مرتبط بتسجيل دخول أو بدون تسجيل دخول، مع السكيما الافتراضية والأدوار. الخصائص تسمح بتغيير السكيما والأدوار وتسجيل الدخول المرتبط.

**الأدوار:** دور جديد مع المالك والأعضاء، وتعديل أعضاء أي دور، بما فيها الأدوار الثابتة مثل db_owner.

**السكيمات:** سكيما جديدة مع المالك، وحذف.

**أدوار السيرفر:** **عرض الأعضاء** يفتح استعلاماً بأعضاء الدور.

</div>

**Logins** (server Security → Logins):
- **New login / Properties** opens a window with four pages:
  - **General:** name, authentication (Windows or SQL), password and confirmation, password policy and expiration, default database.
  - **Server roles:** e.g. sysadmin, dbcreator.
  - **User mapping:** per database, create a user for the login, its default schema and the common roles (db_owner, db_datareader, db_datawriter, db_ddladmin).
  - **Status:** enable or disable the login.
- The password is masked (shown as asterisks) in the script preview.
- Enable, disable, rename, delete.

**Users** (database → Security → Users): new user with or without a login, default schema and roles. Properties change the schema, roles and mapped login.

**Roles:** new role with owner and members, and edit the members of any role, fixed roles such as db_owner included.

**Schemas:** new schema with owner, delete.

**Server roles:** **View members** opens a query listing the role's members.

<a id="f23"></a>
## 23. مهام SQL Server Agent · SQL Server Agent jobs

<div dir="rtl">

في مجلد SQL Server Agent ← المهام المجدولة (غير متاح في إصدار Express): **تشغيل المهمة**، و**إيقافها**، و**تفعيلها** أو **تعطيلها**، و**عرض السجل** (آخر 200 تنفيذ مع الحالة والمدة والرسالة)، و**حذفها**.

</div>

Under SQL Server Agent → Jobs (not in Express): **Start**, **Stop**, **Enable** / **Disable**, **View history** (last 200 runs with status, duration and message) and **Delete**.

<a id="f24"></a>
## 24. مراقب النشاط · Activity Monitor

<div dir="rtl">

زر يمين على السيرفر ← **مراقب النشاط**:

- **بطاقات ملخص:** عدد جلسات المستخدمين، والطلبات النشطة، والجلسات المحجوبة (Blocked)، والمعاملات المفتوحة.
- **العمليات:** كل جلسة بـ SPID، واسم الدخول، والجهاز، والبرنامج، والقاعدة، والحالة، والأمر، ونوع الانتظار، والجلسة الحاجبة، وCPU، والقراءات، والكتابة، والمدة، ونسبة الإنجاز. الجلسات المحجوبة تظهر باللون الأحمر.
  - زر **SQL** يفتح آخر استعلام للجلسة في تبويب جديد.
  - زر **إنهاء** يوقف الجلسة (`KILL`) ويتراجع عن معاملتها المفتوحة.
  - بحث للتصفية بأي قيمة.
- **الاستعلامات الأثقل:** أكثر 30 استعلاماً استهلاكاً للمعالج، مع عدد مرات التنفيذ والمتوسط والقراءات. النقر يفتح الاستعلام.
- **تحديث تلقائي** كل 5 أو 10 أو 30 أو 60 ثانية، ويعمل فقط والتبويب ظاهر.

يحتاج صلاحية `VIEW SERVER STATE`.

</div>

Right-click the server → **Activity Monitor**:

- **Summary tiles:** user sessions, active requests, blocked sessions, open transactions.
- **Processes:** every session with SPID, login, host, program, database, status, command, wait type, blocking session, CPU, reads, writes, elapsed time and percent complete. Blocked sessions are shown in red.
  - **SQL** opens the session's last query in a new tab.
  - **Kill** ends the session (`KILL`) and rolls back its open transaction.
  - A filter box searches any value.
- **Expensive queries:** the top 30 by CPU, with executions, averages and reads. Click one to open it.
- **Auto refresh** every 5, 10, 30 or 60 seconds, only while the tab is visible.

Needs the `VIEW SERVER STATE` permission.

<a id="f25"></a>
## 25. خصائص السيرفر · Server properties

<div dir="rtl">

زر يمين على السيرفر ← **الخصائص**:

- **عام:** اسم الجهاز والمثيل، والإصدار والنسخة، وعدد المعالجات، والذاكرة، ووقت التشغيل، وCollation السيرفر، ووضع المصادقة، وAlways On، والمجلدات الافتراضية للبيانات والسجلات والنسخ الاحتياطي، والنص الكامل لـ `@@VERSION`.
- **الإعدادات:** كل إعدادات `sp_configure` بالقيمة المضبوطة والحالية والحد الأدنى والأقصى والوصف. عدّل القيم ثم **تطبيق التغييرات** (مع `RECONFIGURE`). الإعدادات التي تحتاج إعادة تشغيل SQL Server مُعلّمة، والإعدادات المتقدمة تُفعّل `show advanced options` تلقائياً عند الحاجة.

</div>

Right-click the server → **Properties**:

- **General:** machine and instance, version and edition, processors, memory, start time, server collation, authentication mode, Always On, default data / log / backup folders, and the full `@@VERSION`.
- **Configuration:** every `sp_configure` setting with configured and running values, min, max and description. Edit values and **Apply changes** (with `RECONFIGURE`). Settings that need a restart are marked, and advanced ones turn on `show advanced options` when needed.

<a id="f26"></a>
## 26. مستعرض ملفات السيرفر · Server file browser

<div dir="rtl">

زر **استعراض** في نوافذ النسخ الاحتياطي والاستعادة والإرفاق وإنشاء قاعدة بيانات يعرض الأقراص والمجلدات والملفات **على جهاز SQL Server نفسه** وليس جهازك، لأن SQL Server هو من يقرأ هذه الملفات ويكتبها. يمكنك التنقل بين المجلدات وتصفية الملفات حسب النوع (مثل `.bak` أو `.mdf`).

يحتاج صلاحية `sysadmin`، وبدونها يمكنك كتابة المسار يدوياً. على Docker تكون المسارات بصيغة لينكس، مثل `/var/opt/mssql/data/`.

</div>

The **Browse** button in the backup, restore, attach and new-database dialogs lists drives, folders and files **on the SQL Server machine**, not yours, because SQL Server is the one reading and writing those files. Navigate folders and filter files by type (e.g. `.bak`, `.mdf`).

It needs `sysadmin`; otherwise type the path. In Docker, paths are Linux paths such as `/var/opt/mssql/data/`.

<a id="f27"></a>
## 27. نوافذ التأكيد · Confirmation dialogs

<div dir="rtl">

كل أمر يغيّر شيئاً على السيرفر يفتح نافذة تأكيد فيها:
- شرح لما سيحدث، مع تحذير أحمر للعمليات الخطرة مثل الحذف.
- **معاينة سكربت T-SQL** الذي سيُنفذ، مع زر نسخ.
- **السكربت في استعلام جديد:** يفتح السكربت لمراجعته أو تعديله بدلاً من تنفيذه.
- رسائل SQL Server أثناء التنفيذ، وإشعار في زاوية الشاشة عند النجاح، وتحديث الشجرة تلقائياً.

</div>

Every command that changes the server opens a confirmation window with:
- What will happen, with a red warning for dangerous actions such as deletes.
- A **preview of the T-SQL** it will run, with a copy button.
- **Script to new query** to review or edit the script instead of running it.
- SQL Server's messages while it runs, a notification in the corner on success, and an automatic tree refresh.

---

# ترحيل البيانات · Data Migration

<a id="f28"></a>
## 28. الاتصال بالمصدر والوجهة · Connections

<div dir="rtl">

الصفحة الرئيسية فيها خانتان: **قاعدة بيانات المصدر** (التي تنسخ منها) و**قاعدة بيانات الوجهة** (التي تنسخ إليها).

- الصق نص الاتصال لكل منهما، ويجب أن يحدد اسم القاعدة (`Database=`).
- **اختبار الاتصال** يعرض اسم السيرفر والقاعدة والإصدار والنسخة. يمكن أن يكون المصدر والوجهة على إصدارات مختلفة من SQL Server.
- إذا لم تحدد `Encrypt` و`TrustServerCertificate`، يضيف البرنامج `Encrypt=Optional;TrustServerCertificate=True` حتى تتصل الإصدارات القديمة.
- تنبيه إذا كان المصدر والوجهة نفس القاعدة، والنسخ يكون غير متاح في هذه الحالة.
- **التالي: عرض الجداول** ينتقل لصفحة الجداول.

</div>

The home page has two boxes: the **source database** (copy from) and the **destination database** (copy to).

- Paste each connection string; it must name the database (`Database=`).
- **Test connection** shows the server, database, version and edition. Source and destination can run different SQL Server versions.
- If `Encrypt` and `TrustServerCertificate` aren't set, `Encrypt=Optional;TrustServerCertificate=True` is added so older versions can connect.
- A warning appears when source and destination are the same database, and copying is disabled.
- **Next: view tables** opens the tables page.

<a id="f29"></a>
## 29. اختيار الجداول · Choosing tables

<div dir="rtl">

صفحة الجداول تعرض جداول المصدر بجانب جداول الوجهة المحددة للنسخ:

- كل جدول مع عدد صفوفه وحالته: **موجود في الاثنين**، أو **ناقص** في الوجهة (لا يمكن نسخه)، أو **يقابله** جدول باسم مختلف قليلاً. المطابقة تلقائية مع اختلافات المسافات والمفرد والجمع والسكيما (مثل `Customers` ← `Customer`).
- حدد جداول منفردة، أو سكيما كاملة، أو الكل.
- بحث بالاسم، وفلتر «الناقصة في الوجهة فقط»، وإظهار الجداول التي تم نسخها أو إخفاؤها.
- عدادات: عدد الجداول والصفوف والمحدد للنسخ والناقص في الوجهة.
- الجانب الآخر يعرض الجداول المحددة بعدد صفوفها في المصدر والوجهة، والمنسوخ، والمتجاهَل.

</div>

The tables page shows the source tables beside the destination tables selected for copying:

- Each table with its row count and status: **in both**, **missing** in the destination (can't be copied), or **matched** to a slightly different name. Matching handles spaces, singular / plural and schema differences automatically (e.g. `Customers` → `Customer`).
- Select single tables, a whole schema, or everything.
- Filter by name, show only tables missing in the destination, show or hide tables already copied.
- Counters for tables, rows, selected and missing.
- The other panel lists the selected tables with source rows, destination rows, copied and skipped.

<a id="f30"></a>
## 30. المراجعة قبل النسخ · Review before copying

<div dir="rtl">

عند الضغط على **نسخ**، يقارن البرنامج أعمدة كل جدول في المصدر والوجهة، ويعرض نافذة مراجعة عند وجود ما يستحق الانتباه:

- **أعمدة جديدة في الوجهة:** يتم تجاهلها. وإذا كانت NOT NULL بدون قيمة افتراضية يُعلَّم الجدول «سيفشل» ويتم تخطيه مع استمرار باقي الجداول.
- **أعمدة موجودة في المصدر فقط:** بياناتها لن تُنسخ.
- **أنواع مختلفة:** يتم تحويل القيم.
- **Collation مختلف:** يتم التحويل تلقائياً. وتحذير أحمر إذا كان عمود الوجهة `varchar` بترميز لا يحفظ العربي، فتتحول الحروف إلى `?`.
- **جداول بأسماء مختلفة** وجداول فيها بيانات بالفعل في الوجهة.

</div>

When you press **Copy**, the app compares each table's columns in source and destination, and shows a review window when something needs attention:

- **New columns in the destination** are ignored. If one is NOT NULL without a default, the table is marked "will fail" and skipped while the rest are copied.
- **Columns only in the source**: their data isn't copied.
- **Different types**: values are converted.
- **Different collation**: converted automatically, with a red warning when a destination `varchar` column's encoding can't hold Arabic, so those characters would become `?`.
- **Tables matched by a different name** and tables that already have data in the destination.

<a id="f31"></a>
## 31. النسخ والحفظ والتراجع · Copy, save and roll back

<div dir="rtl">

- كل النسخ يتم داخل **معاملة واحدة** في الوجهة، مع شريط تقدم واسم الجدول الحالي وعدد الصفوف.
- **الصفوف الموجودة مسبقاً** في الوجهة (بنفس المفتاح الأساسي) يتم تجاهلها، ولا يُنسخ إلا الناقص. الجداول بدون مفتاح أساسي تُقارن بالصف كاملاً.
- خيار **حذف البيانات الموجودة في جداول الوجهة قبل النسخ**.
- **إيقاف:** يوقف النسخ في أي وقت.
- بعد الانتهاء تبقى البيانات في معاملة غير محفوظة، والجداول مقفولة في الوجهة حتى تختار:
  - **حفظ نهائي (Commit):** يثبّت البيانات، وتظهر نافذة النتيجة بعدد السجلات المحفوظة والجداول الناجحة والمتجاهَلة والفاشلة.
  - **تراجع (Rollback):** يلغي كل ما حدث في الوجهة.
  - إغلاق المتصفح قبل الاختيار يلغي المعاملة تلقائياً.

</div>

- Everything is copied inside **one transaction** in the destination, with a progress bar, the current table and row counts.
- **Rows that already exist** in the destination (same primary key) are skipped; only missing rows are copied. Tables without a primary key are compared on the whole row.
- Option to **delete the existing data in the destination tables first**.
- **Stop** halts the copy at any time.
- When it finishes, the data waits in an uncommitted transaction, and the copied tables stay locked in the destination until you choose:
  - **Save (Commit)** keeps the data and shows a summary: records saved, tables that succeeded, skipped and failed.
  - **Rollback** undoes everything done in the destination.
  - Closing the browser before choosing rolls it back.

<a id="f32"></a>
## 32. الربط مع إدارة قواعد البيانات · Link with the database manager

<div dir="rtl">

- في مستكشف الكائنات: زر يمين على قاعدة بيانات ← **استخدامها في ترحيل البيانات** ← **كمصدر** أو **كوجهة**. يعبّئ البرنامج نص الاتصال ويختبره، وينقلك لصفحة الترحيل.
- في نافذة الاتصال بسيرفر: زر لاستخدام اتصال المصدر أو الوجهة الموجود في صفحة الترحيل.

</div>

- In the Object Explorer: right-click a database → **Use for data migration** → **as source** or **as destination**. The connection string is filled in and tested, and the migration page opens.
- In the connect dialog: buttons reuse the source or destination connection from the migration page.

---

# مرجع · Reference

<a id="shortcuts"></a>
## اختصارات لوحة المفاتيح · Keyboard shortcuts

| الاختصار · Shortcut | الوظيفة | Action |
|---|---|---|
| `F5` / `Ctrl+E` | تنفيذ الاستعلام أو النص المحدد | Run the query or the selection |
| `Ctrl+F5` | فحص الصياغة | Parse |
| `Alt+Break` | إلغاء التنفيذ | Cancel |
| `Ctrl+/` | تعليق / إلغاء تعليق الأسطر | Comment / uncomment lines |
| `Ctrl+S` | حفظ السكربت | Save the script |
| `Tab` / `Shift+Tab` | إزاحة الأسطر | Indent / outdent |
| `Enter` / `F2` على خلية · on a cell | عرض / تعديل القيمة | View / edit the value |
| `Ctrl+C` على خلية · on a cell | نسخ القيمة | Copy the value |
| `F5` في الشجرة · in the tree | تحديث العنصر | Refresh the node |
| `Esc` | إغلاق النافذة أو القائمة | Close a dialog or menu |
| زر الماوس الأوسط على تبويب · Middle-click a tab | إغلاق التبويب | Close the tab |

<a id="notes"></a>
## ملاحظات · Notes

<div dir="rtl">

- **الخصوصية:** الاتصالات وكلمات المرور تبقى في الذاكرة فقط ولا تُحفظ على القرص. يُحفظ فقط اللغة والوضع وأسماء السيرفرات الأخيرة، بدون كلمات مرور.
- **Docker ولينكس:** لا يدعمان مصادقة ويندوز. استخدم مصادقة SQL Server.
- **Smart App Control** في ويندوز قد يمنع تشغيل نسخ البناء غير الموقّعة برسالة <span dir="ltr">*"An Application Control policy has blocked this file"*</span>. أعد البناء (مثلاً بإعدادات Release أو Debug الأخرى) أو وقّع البرنامج بشهادة Code Signing. لا يُنصح بإيقاف Smart App Control لأنه لا يمكن إعادة تفعيله إلا بإعادة تثبيت ويندوز.
- **الصلاحيات:** بعض الميزات تحتاج صلاحيات على السيرفر، مثل `sysadmin` لاستعراض الملفات و`VIEW SERVER STATE` لمراقب النشاط.

</div>

- **Privacy:** connections and passwords are kept in memory only and never written to disk. Only the language, the theme and recent server names (no passwords) are saved.
- **Docker and Linux** don't support Windows Authentication; use SQL Server Authentication.
- Windows **Smart App Control** may block unsigned builds with *"An Application Control policy has blocked this file"*. Rebuild (e.g. the other configuration) or sign the app with a code-signing certificate. Turning Smart App Control off isn't recommended: it can only be turned back on by reinstalling Windows.
- **Permissions:** some features need server permissions, such as `sysadmin` for browsing files and `VIEW SERVER STATE` for the Activity Monitor.

<a id="structure"></a>
## هيكل المشروع · Project structure

| المجلد · Folder | المحتوى | Contents |
|---|---|---|
| `Components/Pages` | الصفحات: الترحيل (`Home` و`Tables`) وإدارة القواعد (`Manager`) | Pages: migration (`Home`, `Tables`) and database manager (`Manager`) |
| `Components/Manager` | واجهة الإدارة: المستكشف والمحرر والمصمم والنوافذ | Manager UI: Object Explorer, query editor, designers, dialogs |
| `Components/Shared` | واجهة الترحيل | Migration UI |
| `Services/Manager` | منطق الإدارة: تنفيذ الاستعلامات والبيانات الوصفية والسكربتات والتعديل والإدارة | Manager logic: query runner, metadata, scripting, data editing, administration |
| `Services` | منطق الترحيل (`SqlServerService` و`TableCopyJob`) | Migration logic (`SqlServerService`, `TableCopyJob`) |
| `Models` | نماذج البيانات | Data models |
| `Localization` | النصوص العربية والإنجليزية (`Texts.cs` و`ManagerTexts.cs`) | Arabic / English texts (`Texts.cs`, `ManagerTexts.cs`) |
| `Desktop` | نافذة سطح المكتب (WinForms + WebView2) | Desktop window (WinForms + WebView2) |
| `wwwroot` | التنسيقات والسكربتات والأيقونة | Styles, scripts, icon |
| `publish-desktop.ps1` | نشر وتثبيت برنامج سطح المكتب | Publish and install the desktop app |
