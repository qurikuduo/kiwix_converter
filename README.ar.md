# Kiwix Converter

Kiwix Converter هو تطبيق سطح مكتب مبني على WinForms و SQLite لتحويل ملفات ZIM التي تم تنزيلها عبر kiwix-desktop إلى ملفات Markdown على مستوى المقالات وملفات JSON جاهزة لأنظمة RAG.

## نسخ اللغات

- English: [README.md](README.md)
- 简体中文: [README.zh-CN.md](README.zh-CN.md)
- 日本語: [README.ja.md](README.ja.md)
- Español: [README.es.md](README.es.md)
- العربية: [README.ar.md](README.ar.md)

## القدرات الأساسية

- فحص مجلد kiwix-desktop الذي تم تكوينه ومزامنة ملفات ZIM المتاحة.
- استخدام `zimdump` لقراءة البيانات الوصفية وقوائم المقالات واستخراج HTML والصور.
- استخراج المحتوى الرئيسي فقط من المقالة وإعادة كتابة الروابط الداخلية إلى مسارات Markdown محلية.
- إنشاء `content.md` و `metadata.json` و `chunks.jsonl` لتغذية أنظمة RAG.
- حفظ المهام والسجلات ونقاط التوقف لكل مقالة داخل SQLite لدعم الإيقاف والاستئناف واسترجاع الجلسات.

## المتطلبات

- Windows
- .NET 8 SDK
- توفر `zimdump` في `PATH` أو تحديد مساره من داخل الواجهة

## طريقة الاستخدام

1. شغّل التطبيق.
2. عند التشغيل لأول مرة قم بتحديد:
   - مجلد `kiwix-desktop`
   - مجلد الإخراج الافتراضي
   - مسار `zimdump` إذا لم يكن موجوداً في `PATH`
3. اضغط `Scan ZIM Files` لمزامنة الملفات المحلية.
4. اختر ملف ZIM من القائمة، ويمكنك تحديد مجلد إخراج خاص بهذه المهمة إذا أردت.
5. ابدأ التحويل وتابع التقدم والسجل والتاريخ من واجهة البرنامج.

## الأتمتة وعمليات الإصدار

- [`.github/workflows/ci.yml`](.github/workflows/ci.yml) تقوم بالبناء ورفع الحزمة عند كل push إلى `main` وعند كل pull request.
- [`.github/workflows/release.yml`](.github/workflows/release.yml) تقوم بإنشاء الحزمة ونشر إصدار تلقائي بإصدارات semantic versioning.
- [`.github/release.yml`](.github/release.yml) يحدد قالب release notes التي يتم توليدها تلقائياً.

## مصادر الويكي

- يتم حفظ صفحات الويكي متعددة اللغات في [docs/wiki](docs/wiki).
- تتضمن حالياً صفحة رئيسية وصفحة خاصة بعملية الإصدار باللغات الإنجليزية والصينية واليابانية والإسبانية والعربية.