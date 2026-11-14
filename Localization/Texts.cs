namespace MigrateSQLServerDatabases.Localization;

public static class Texts
{
    public static readonly IReadOnlyDictionary<string, (string En, string Ar)> All = new Dictionary<string, (string En, string Ar)>
    {
        // Layout
        ["AppName"] = ("SQL Server Manager", "إدارة SQL Server"),
        ["OtherLanguage"] = ("العربية", "English"),
        ["StepConnections"] = ("Connections", "الاتصال"),
        ["StepTables"] = ("Tables", "الجداول"),
        ["ConnectFirst"] = ("Connect to both databases first", "اتصل بقاعدتي البيانات أولاً"),
        ["UnhandledError"] = ("An unhandled error has occurred.", "حدث خطأ غير متوقع."),
        ["Reload"] = ("Reload", "إعادة التحميل"),

        // Connections page
        ["HomeTitle"] = ("Connect your databases", "اتصل بقواعد البيانات"),
        ["HomeLead"] = (
            "Enter the connection strings for the database you copy from and the one you copy to. They can be on different SQL Server versions.",
            "أدخل نص الاتصال لقاعدة البيانات التي ستنسخ منها والتي ستنسخ إليها. يمكن أن يكونا على إصدارات مختلفة من SQL Server."),
        ["SameDatabaseWarning"] = (
            "Source and destination point to the same server and database.",
            "المصدر والوجهة يشيران إلى نفس السيرفر ونفس قاعدة البيانات."),
        ["Connecting"] = ("Connecting…", "جارٍ الاتصال…"),
        ["NextTables"] = ("Next: view tables →", "التالي: عرض الجداول ←"),

        // Connection card
        ["SourceDatabase"] = ("Source database", "قاعدة بيانات المصدر"),
        ["DestinationDatabase"] = ("Destination database", "قاعدة بيانات الوجهة"),
        ["CopyFrom"] = ("Copy from", "النسخ منها"),
        ["CopyTo"] = ("Copy to", "النسخ إليها"),
        ["ConnectionString"] = ("Connection string", "نص الاتصال (Connection string)"),
        ["EncryptHint"] = (
            "When Encrypt and TrustServerCertificate are not set, this is added so older SQL Server versions can connect:",
            "إذا لم يتم تحديد Encrypt و TrustServerCertificate تتم إضافة ما يلي حتى تتمكن الإصدارات القديمة من SQL Server من الاتصال:"),
        ["TestConnection"] = ("Test connection", "اختبار الاتصال"),
        ["Testing"] = ("Testing…", "جارٍ الاختبار…"),
        ["Connected"] = ("Connected", "متصل"),
        ["ConnectionFailed"] = ("Connection failed", "فشل الاتصال"),
        ["Server"] = ("Server", "السيرفر"),
        ["Database"] = ("Database", "قاعدة البيانات"),
        ["Version"] = ("Version", "الإصدار"),
        ["Edition"] = ("Edition", "النسخة"),
        ["MissingDatabase"] = (
            "The connection string must specify a database (Database / Initial Catalog).",
            "يجب أن يحدد نص الاتصال اسم قاعدة البيانات (Database / Initial Catalog)."),

        // Tables page
        ["TablesTitle"] = ("Tables", "الجداول"),
        ["TablesLead"] = (
            "All user tables in the source database next to the ones in the destination.",
            "كل جداول قاعدة بيانات المصدر بجانب جداول قاعدة بيانات الوجهة."),
        ["BackToConnections"] = ("← Connections", "→ الاتصال"),
        ["Refresh"] = ("Refresh", "تحديث"),
        ["SourceTables"] = ("Source tables", "جداول المصدر"),
        ["DestinationTables"] = ("Destination tables", "جداول الوجهة"),

        // Tables panel
        ["Source"] = ("Source", "المصدر"),
        ["Destination"] = ("Destination", "الوجهة"),
        ["LoadingTables"] = ("Loading tables…", "جارٍ تحميل الجداول…"),
        ["LoadTablesFailed"] = ("Could not load tables", "تعذر تحميل الجداول"),
        ["FilterTables"] = ("Filter tables…", "بحث في الجداول…"),
        ["OnlyMissingInDestination"] = ("Only missing in destination", "الناقصة في الوجهة فقط"),
        ["OnlyNotInSource"] = ("Only not in source", "غير الموجودة في المصدر فقط"),
        ["TablesCount"] = ("tables", "جدول"),
        ["RowsCount"] = ("rows", "صف"),
        ["MissingInDestination"] = ("missing in destination", "ناقص في الوجهة"),
        ["NotInSource"] = ("not in source", "غير موجود في المصدر"),
        ["ColTable"] = ("Table", "الجدول"),
        ["ColRows"] = ("Rows", "الصفوف"),
        ["ColStatus"] = ("Status", "الحالة"),
        ["InBoth"] = ("In both", "موجود في الاثنين"),
        ["Missing"] = ("Missing", "ناقص"),
        ["OnlyHere"] = ("Only here", "هنا فقط"),
        ["NoTablesMatch"] = ("No tables match.", "لا توجد جداول مطابقة."),
        ["SelectAll"] = ("Select all", "تحديد الكل"),
        ["SelectSchema"] = ("Select the whole schema", "تحديد السكيما بالكامل"),
        ["ShowCopiedTables"] = ("Show copied tables ({0})", "إظهار الجداول المنسوخة ({0})"),
        ["CopiedTablesLabel"] = ("copied", "تم نسخها"),
        ["RenamedTablesLabel"] = ("matched by a different name", "مطابقة باسم مختلف"),
        ["MatchedTo"] = ("Matches", "يقابله"),
        ["RenamedHint"] = (
            "The destination table has a slightly different name (spaces, singular/plural or schema) and was matched automatically:",
            "اسم الجدول في الوجهة مختلف قليلاً (مسافات أو مفرد/جمع أو سكيما) وتمت مطابقته تلقائياً:"),
        ["FromSourceTable"] = ("from", "من"),
        ["RenamedNote"] = (
            "These tables have a slightly different name in the destination and will be copied into the matching table:",
            "هذه الجداول لها اسم مختلف قليلاً في الوجهة، وسيتم نسخها إلى الجدول المقابل:"),
        ["SelectedForCopy"] = ("selected", "محدد للنسخ"),
        ["NotInDestinationCantCopy"] = (
            "This table doesn't exist in the destination, so its data can't be copied.",
            "هذا الجدول غير موجود في الوجهة، لذلك لا يمكن نسخ بياناته."),
        ["SameDatabaseCopyBlocked"] = (
            "Source and destination are the same database, so copying is disabled.",
            "المصدر والوجهة نفس قاعدة البيانات، لذلك النسخ غير متاح."),
        ["LockedWhileCopying"] = (
            "Connections can't be changed while a copy is running or waiting for save / rollback.",
            "لا يمكن تعديل الاتصال أثناء وجود عملية نسخ جارية أو في انتظار الحفظ أو التراجع."),

        // Selected tables (destination side)
        ["TablesToCopy"] = ("Tables to copy", "الجداول المحددة للنسخ"),
        ["SelectTablesHint"] = (
            "Tick tables in the source list and they will appear here.",
            "حدد الجداول من قائمة المصدر وستظهر هنا."),
        ["ColSourceRows"] = ("Source rows", "سجلات المصدر"),
        ["ColDestinationRows"] = ("In destination", "الموجود في الوجهة"),
        ["ColCopied"] = ("Copied", "تم نسخه"),
        ["CopiedLabel"] = ("copied", "تم نسخه"),
        ["ColSkipped"] = ("Skipped", "تم تجاهله"),
        ["SkippedLabel"] = ("skipped (already exist)", "تم تجاهلها (موجودة مسبقاً)"),
        ["SkippedHint"] = (
            "Rows not copied because the destination already has a row with the same Id (primary key)",
            "صفوف لم تُنسخ لأن الوجهة فيها صف بنفس الـ Id (المفتاح الأساسي)"),
        ["ExistingRowsNote"] = (
            "{0} of the selected tables already have data in the destination. Every row is checked by its Id (primary key): rows that already exist are skipped and only the missing ones are copied. Tables without a primary key are compared on the whole row.",
            "{0} من الجداول المحددة فيها بيانات بالفعل في الوجهة. سيتم فحص كل صف بالـ Id (المفتاح الأساسي): الصفوف الموجودة سيتم تجاهلها ونسخ الناقصة فقط. الجداول التي ليس لها مفتاح أساسي تتم مقارنتها بالصف كاملاً."),
        ["StatusPending"] = ("Pending", "في الانتظار"),
        ["StatusClearing"] = ("Clearing", "جارٍ الحذف"),
        ["StatusCopying"] = ("Copying", "جارٍ النسخ"),
        ["StatusCopied"] = ("Copied", "تم النسخ"),
        ["StatusSaved"] = ("Saved", "تم الحفظ"),
        ["StatusStopped"] = ("Stopped", "تم الإيقاف"),
        ["StatusFailed"] = ("Failed", "فشل"),
        ["StatusRolledBack"] = ("Rolled back", "تم التراجع"),

        // Copy controls
        ["Copy"] = ("Copy", "نسخ"),
        ["Stop"] = ("Stop", "إيقاف"),
        ["Rollback"] = ("Rollback", "تراجع (Rollback)"),
        ["Commit"] = ("Save (Commit)", "حفظ نهائي (Commit)"),
        ["ClearDestination"] = ("Delete existing data in the destination tables first", "حذف البيانات الموجودة في جداول الوجهة قبل النسخ"),
        ["CurrentTable"] = ("Current table", "الجدول الحالي"),
        ["StateIdle"] = ("Ready to copy", "جاهز للنسخ"),
        ["StateCopying"] = ("Copying…", "جارٍ النسخ…"),
        ["StateStopping"] = ("Stopping…", "جارٍ الإيقاف…"),
        ["StateAwaitingDecision"] = ("Copy finished — waiting for save or rollback", "انتهى النسخ — في انتظار الحفظ أو التراجع"),
        ["StateCommitting"] = ("Saving…", "جارٍ الحفظ…"),
        ["StateRollingBack"] = ("Rolling back…", "جارٍ التراجع…"),
        ["StateCommitted"] = ("Data saved in the destination database", "تم حفظ البيانات في قاعدة الوجهة"),
        ["StateRolledBack"] = ("All changes in the destination were rolled back", "تم التراجع عن كل التغييرات في قاعدة الوجهة"),
        ["StateFailed"] = ("The copy failed and all changes were rolled back", "فشلت العملية وتم التراجع عن كل التغييرات"),
        ["AwaitingDecisionHint"] = (
            "The data is inside a transaction that is not saved yet, and the copied tables stay locked in the destination until you choose. Press Save to keep it or Rollback to undo it. Closing the browser rolls it back.",
            "البيانات منسوخة داخل معاملة (Transaction) لم تُحفظ بعد، والجداول المنسوخة تظل مقفولة في الوجهة حتى تختار. اضغط «حفظ نهائي» لتثبيتها أو «تراجع» لإلغائها. إغلاق المتصفح يلغيها تلقائياً."),
        ["ConfirmClear"] = (
            "Existing data in the selected destination tables will be deleted before copying (you can still roll back). Continue?",
            "سيتم حذف البيانات الموجودة في جداول الوجهة المحددة قبل النسخ (ويمكنك التراجع لاحقاً). هل تريد المتابعة؟"),
        // Review before copying
        ["Comparing"] = ("Comparing columns…", "جارٍ مقارنة الحقول…"),
        ["CompareFailed"] = ("Could not compare the columns", "تعذرت مقارنة الحقول"),
        ["ReviewTitle"] = ("Review before copying", "مراجعة قبل النسخ"),
        ["ColumnDiffIntro"] = (
            "Columns differ between the source and destination in {0} of the selected tables:",
            "توجد اختلافات في الحقول بين المصدر والوجهة في {0} من الجداول المحددة:"),
        ["ColColumn"] = ("Column", "الحقل"),
        ["ColDifference"] = ("Difference", "الاختلاف"),
        ["DiffDestinationOnly"] = ("New in destination — ignored", "جديد في الوجهة — سيتم تجاهله"),
        ["DiffDestinationOnlyBlocking"] = (
            "New in destination, NOT NULL without a default — this table will fail",
            "جديد في الوجهة و NOT NULL بدون قيمة افتراضية — نسخ هذا الجدول سيفشل"),
        ["DiffSourceOnly"] = ("Only in source — its data won't be copied", "موجود في المصدر فقط — بياناته لن تُنسخ"),
        ["DiffTypeMismatch"] = ("Different type — values will be converted", "النوع مختلف — سيتم تحويل القيم"),
        ["DiffTypeMismatchLossy"] = ("Different type — some characters may become ?", "النوع مختلف — قد تتحول بعض الحروف إلى ؟"),
        ["DiffCollationLossy"] = ("Different collation — some characters may become ?", "ترتيب حروف (Collation) مختلف — قد تتحول بعض الحروف إلى ؟"),
        ["CollationNote"] = (
            "{0} text columns use a different collation. They are converted automatically while copying, without losing data:",
            "{0} حقل نصي له ترتيب حروف (Collation) مختلف، وسيتم تحويلها تلقائياً أثناء النسخ بدون فقد للبيانات:"),
        ["LossyNote"] = (
            "Columns marked in red are varchar in the destination with an encoding that can't hold every character (e.g. Arabic), so those characters will be saved as \"?\". To avoid this, change the destination column to nvarchar or to an Arabic collation.",
            "الحقول المعلّمة بالأحمر من نوع varchar في الوجهة بترميز لا يدعم كل الحروف (مثل العربي)، فالحروف غير المدعومة ستُحفظ كـ «?». لتجنب ذلك غيّر الحقل في الوجهة إلى nvarchar أو إلى Collation عربي."),
        ["WillFail"] = ("Will fail", "سيفشل"),
        ["IgnoreNewColumnsNote"] = (
            "The data will be copied and the new columns that don't exist in the source will be ignored (left empty or given their default value).",
            "سيتم نسخ البيانات مع تجاهل الحقول الجديدة غير الموجودة في المصدر (ستبقى فارغة أو تأخذ قيمتها الافتراضية)."),
        ["BlockingColumnsNote"] = (
            "Tables marked \"Will fail\" have a new NOT NULL column without a default value, so they will be skipped while the other tables are copied. To copy them, give the column a default value or allow NULL in the destination.",
            "الجداول المعلّمة «سيفشل» فيها حقل جديد NOT NULL بدون قيمة افتراضية، لذلك سيتم تخطيها مع استمرار نسخ باقي الجداول. لنسخها أضف قيمة افتراضية للحقل أو اسمح بـ NULL في الوجهة."),
        ["Cancel"] = ("Cancel", "إلغاء"),
        ["ContinueCopy"] = ("Continue copying", "متابعة النسخ"),

        // Result dialog (after Save / Commit)
        ["ResultCommittedTitle"] = ("Data saved successfully", "تم حفظ البيانات بنجاح"),
        ["ResultCommittedPartialTitle"] = ("Data saved — some tables were not copied", "تم حفظ البيانات — بعض الجداول لم تُنسخ"),
        ["ResultCommitFailedTitle"] = ("Saving failed", "فشل حفظ البيانات"),
        ["ResultSavedSummary"] = ("{0} records were saved in {1} tables in the destination database.", "تم حفظ {0} سجل في {1} جدول في قاعدة الوجهة."),
        ["ResultCommitFailedSummary"] = (
            "The data could not be saved, so all changes in the destination database were rolled back.",
            "تعذر حفظ البيانات، لذلك تم التراجع عن كل التغييرات في قاعدة الوجهة."),
        ["StatRowsSaved"] = ("Records saved", "سجلات تم حفظها"),
        ["StatTablesSucceeded"] = ("Tables succeeded", "جداول نجحت"),
        ["StatRowsSkipped"] = ("Already existed (skipped)", "موجودة مسبقاً (تم تجاهلها)"),
        ["StatTablesFailed"] = ("Tables failed / stopped", "جداول فشلت / توقفت"),
        ["FailedTablesTitle"] = ("Tables not copied:", "الجداول التي لم تُنسخ:"),
        ["Close"] = ("Close", "إغلاق"),

        ["ConfirmRollback"] = (
            "Undo everything this copy did in the destination?",
            "هل تريد التراجع عن كل ما تم في قاعدة الوجهة خلال عملية النسخ هذه؟"),
    };
}
