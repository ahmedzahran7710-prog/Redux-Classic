# Comet 5187 - AutoPatcher & Launcher Wrapper

مشروع أوتوباتشر ولانشر خفيف مكتوب بـ C# (`.NET 6`) مخصص لربط كلينت **Conquer Online 5187** مع:
1. **Dragon Launcher (`Dragon.Launch.exe`)** الموجود في `tools/DragonLauncher`.
2. أو **ConquerLoader (`ConquerLoader.exe`)** مباشرة لو حبيت تستخدمه.

## المميزات
- **وضع الكلينت الخام (افتراضي):** طالما `EnableAutoPatch: false` في `PatcherConfig.json`، بيشغل الكلينت مباشرة بدون الحاجة لرفع باتشات.
- **وضع الأوتوباتش (`EnableAutoPatch: true`):** يقرأ ملف `patches.json` من موقعك/سيرفرك، يحمل التحديثات بصيغة `.zip` بالترتيب (مثلاً `1001.zip`, `1002.zip`)، يتحقق من بصمة الأمان **SHA-256**، يفك الضغط داخل الكلينت، ثم يشغل اللودر تلقائياً.
- **أداة توليد `patches.json` للأدمن:**
  ```bash
  dotnet run --project tools/Comet.AutoPatcher -- --generate-manifest ./my_patches https://yourdomain.com/patches
  ```
