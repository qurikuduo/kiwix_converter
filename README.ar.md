# Kiwix Converter

[![CI](https://github.com/qurikuduo/kiwix_converter/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/qurikuduo/kiwix_converter/actions/workflows/ci.yml)
[![Release Workflow](https://github.com/qurikuduo/kiwix_converter/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/qurikuduo/kiwix_converter/actions/workflows/release.yml)
[![Latest Release](https://img.shields.io/github/v/release/qurikuduo/kiwix_converter?display_name=tag&sort=semver)](https://github.com/qurikuduo/kiwix_converter/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-F2C94C.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=.net)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D6?logo=windows11)](https://github.com/qurikuduo/kiwix_converter/releases/latest)
[![UI Languages](https://img.shields.io/badge/UI%20Languages-English%20%7C%20%E7%AE%80%E4%BD%93%E4%B8%AD%E6%96%87%20%7C%20%E6%97%A5%E6%9C%AC%E8%AA%9E%20%7C%20Espa%C3%B1ol%20%7C%20%D8%A7%D9%84%D8%B9%D8%B1%D8%A8%D9%8A%D8%A9-0A7C86)](#نسخ-اللغات)

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
- تحتاج إلى .NET 8 Desktop Runtime لتشغيل النسخة المعبأة من التطبيق
- تحتاج إلى .NET 8 SDK إذا كنت ستبني المشروع من المصدر
- توفر `zimdump` في `PATH` أو تحديد مساره من داخل الواجهة

## ملفات تشغيل سطح المكتب

- يحاول التطبيق المعبأ حفظ بيانات التشغيل بجانب ملف EXE حتى تبقى الحزمة قابلة للحمل وسهلة الفحص.
- يتم حفظ إعدادات SQLite وحالة المهام في `data/kiwix-converter.db`.
- تتم كتابة سجلات بدء التشغيل والتتبع أثناء التشغيل في `logs/kiwix-converter-YYYY-MM-DD.log`.
- إذا كان مجلد النشر غير قابل للكتابة، يعود التطبيق إلى `%LocalAppData%\KiwixConverter`.

## لقطة شاشة

توضح الصورة التالية الواجهة الحقيقية الملتقطة من نسخة Windows المنشورة الحالية.

![النافذة الرئيسية لتطبيق Kiwix Converter](docs/images/app-main-window.png)

## تصميم البنية

- `KiwixConverter.WinForms` يتولى غلاف سطح المكتب، وإدخال الإعدادات، وجداول المهام، وعرض الحالة، وتدفق تشغيل المستخدم.
- `KiwixConverter.Core` يتولى الفحص والتحويل والمزامنة مع WeKnora والاستمرارية عبر SQLite حتى تبقى طبقة الواجهة خفيفة.
- `zimdump` هو حد الوصول إلى أرشيفات ZIM للبيانات الوصفية وقوائم المقالات و HTML والموارد.
- واجهة WeKnora HTTP API هي حد مزامنة RAG لاكتشاف قواعد المعرفة وتحميل النماذج وإنشاء KB ورفع المقالات.
- الأعمال الطويلة تُنمذج كمهام محفوظة مع نقاط توقف على مستوى المقالة، لذلك لا تفرض إعادة التشغيل إعادة تحويل الأرشيف بالكامل.

## التدفق التقني

1. يقوم فحص المجلد بعمل upsert لقائمة ملفات ZIM المحلية داخل SQLite قبل بدء أي تحويل.
2. يستخدم التحويل `zimdump` لجلب البيانات الوصفية و HTML، ثم يستخرج المحتوى الرئيسي ويعيد كتابة الروابط ويصدر الصور وينتج ملفات Markdown و JSON.
3. تحفظ كل مقالة ملفات `content.md` و `metadata.json` و `chunks.jsonl` وحالة checkpoint الخاصة بها، لذلك يعاد فقط الجزء المحلي الذي فشل بالفعل.
4. تقرأ مزامنة WeKnora المخرجات المكتملة، وتحمل معرّفات النماذج الحية من `/api/v1/models`، وتقوم بحل أو إنشاء قواعد معرفة بإعدادات chunk، ثم ترفع Markdown لكل مقالة كمعرفة يدوية قابلة للاستئناف.

## بدء سريع للمستخدمين غير التقنيين

إذا كنت تريد فقط استخدام التطبيق، فالطريقة الأسهل هي تنزيل ملف Windows zip من GitHub Releases وتثبيت .NET 8 Desktop Runtime. لا تحتاج إلى SDK الكامل إلا إذا كنت ستفتح الحل في Visual Studio أو ستبني المشروع بنفسك عبر `dotnet build`.

### 1. تثبيت .NET

- لتشغيل التطبيق المعبأ: ثبّت .NET 8 Desktop Runtime لنظام Windows x64.
- للبناء من المصدر: ثبّت .NET 8 SDK.
- بعد التثبيت، أعد فتح الطرفية أو التطبيق حتى يصبح أمر `dotnet` متاحاً عبر `PATH`.

### 2. تثبيت `zimdump`

لا يقرأ Kiwix Converter ملفات ZIM مباشرة، بل يعتمد على `zimdump` القادم مع أدوات Kiwix.

الإعداد المعتاد على Windows:

1. نزّل حزمة أدوات Kiwix التي تحتوي على `zimdump.exe`.
2. فك الضغط في مجلد ثابت مثل `C:\Kiwix\tools\`.
3. اختر إحدى الطريقتين التاليتين:
   - أضف ذلك المجلد إلى `PATH` في Windows
   - أو اترك الملف في مكانه وحدد `zimdump.exe` يدوياً عند أول تشغيل للتطبيق

### 3. فحص الاعتمادات عند التشغيل

يقوم التطبيق الآن بالتحقق من `zimdump` أثناء بدء التشغيل.

- إذا كان `zimdump` متاحاً، يمكنك بدء التحويل مباشرة.
- إذا لم يكن موجوداً، فسيعرض التطبيق تحذيراً ويسمح لك باختيار `zimdump.exe` فوراً.
- يمكن أن يبقى التطبيق مفتوحاً بدون `zimdump`، لكن التحويل واستخراج البيانات الوصفية سيظلان معطلين حتى يتم ضبطه.

### 4. إعداد المزامنة مع WeKnora

أول هدف مدمج للمزامنة مع أنظمة RAG هو WeKnora.

في قسم `WeKnora Sync Configuration` قم بإعداد:

- عنوان WeKnora الأساسي
- وضع المصادقة: `API Key` أو `Bearer Token`
- رمز الوصول
- معرّف قاعدة المعرفة أو اسمها
- معرّفات النماذج الاختيارية `KnowledgeQA` و `Embedding` و `VLLM` التي يمكن الحصول عليها من `/api/v1/models`
- ما إذا كان التطبيق مسموحاً له بإنشاء قاعدة المعرفة تلقائياً عندما لا يكون الاسم موجوداً

واجهة المزامنة تسمح لك بـ:

- تحميل قائمة قواعد المعرفة من الخادم
- اختبار الاتصال قبل بدء المزامنة
- إعادة تطبيق نماذج الدردشة و Embedding ومتعدد الوسائط المهيأة عند إنشاء قاعدة معرفة أو بدء مزامنة
- اختيار مخرجات التحويل المكتملة التي تريد إرسالها إلى WeKnora
- متابعة السجل، وسجلات التشغيل، وشريط التقدم، و ETA، وحالة الإيقاف والاستئناف

## طريقة الاستخدام

1. إذا كنت تستخدم الحزمة المنشورة فثبّت أولاً .NET 8 Desktop Runtime، وإذا كنت تعمل من المصدر فثبّت .NET 8 SDK.
2. تأكد من تثبيت `zimdump`.
3. شغّل التطبيق.
4. عند التشغيل لأول مرة قم بتحديد:
   - مجلد `kiwix-desktop`
   - مجلد الإخراج الافتراضي
   - مسار `zimdump` إذا لم يكن موجوداً في `PATH`
5. إذا أظهر فحص التشغيل أن `zimdump` غير موجود، فقم بتصحيح `PATH` أو اختر `zimdump.exe` يدوياً.
6. اضغط `Scan ZIM Files` لمزامنة الملفات المحلية.
7. اختر ملف ZIM من القائمة، ويمكنك تحديد مجلد إخراج خاص بهذه المهمة إذا أردت.
8. ابدأ التحويل وتابع التقدم والسجل والتاريخ من واجهة البرنامج.
9. لإرسال المقالات إلى WeKnora، افتح صفحة `WeKnora Sync` وحدد مخرج تحويل واحداً أو أكثر ثم ابدأ مهمة مزامنة.

## الأتمتة وعمليات الإصدار

- [`.github/workflows/ci.yml`](.github/workflows/ci.yml) تقوم بالبناء ورفع الحزمة عند كل push إلى `main` وعند كل pull request.
- [`.github/workflows/release.yml`](.github/workflows/release.yml) تقوم الآن بنشر GitHub Release تلقائي عند كل push إلى `main` مع حساب رقم patch التالي اعتماداً على أحدث وسم إصدار موجود.
- ما زال نفس workflow يدعم `workflow_dispatch` إذا احتجت إلى تحديد رقم الإصدار يدوياً.
- [`.github/release.yml`](.github/release.yml) يحدد قالب release notes التي يتم توليدها تلقائياً.

## مصادر الويكي

- يتم حفظ صفحات الويكي متعددة اللغات في [docs/wiki](docs/wiki).
- تتضمن حالياً صفحة رئيسية وصفحة خاصة بعملية الإصدار باللغات الإنجليزية والصينية واليابانية والإسبانية والعربية.

## الترخيص

هذا المشروع متاح بموجب ترخيص MIT. راجع [LICENSE](LICENSE).