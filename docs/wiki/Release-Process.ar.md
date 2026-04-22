# عملية الإصدار

## الترقيم

- تتبع الإصدارات وسوماً semantic versioning مثل `v0.1.2`.
- بالنسبة لإصلاحات CI/CD أو التغليف، يفضل زيادة رقم patch.
- يجب أن تعكس إصدارات major و minor التغييرات الموجهة للمستخدم.

## مسار GitHub Actions

1. يتم تشغيل `ci.yml` عند كل push إلى `main` وعند كل pull request.
2. يتم تشغيل `release.yml` الآن عند كل push إلى `main`، ويحسب تلقائياً رقم patch التالي اعتماداً على أحدث وسم release، كما يمكن تشغيله أيضاً عبر `workflow_dispatch` مع إدخال نسخة محددة يدوياً.
3. يقوم مسار الإصدار ببناء تطبيق WinForms، ونشر حزمة Windows، وإنشاء checksum، ثم إنشاء GitHub Release.

## ملفات الإصدار

- `KiwixConverter-win-x64-vX.Y.Z.zip`
- `KiwixConverter-win-x64-vX.Y.Z.zip.sha256`

## ملاحظات الإصدار

يتم توليد release notes تلقائياً عبر `.github/release.yml` مع تقسيم تقليدي إلى ميزات جديدة وإصلاحات ووثائق وأعمال صيانة.