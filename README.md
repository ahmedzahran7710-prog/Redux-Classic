# MoonQueen — Conquer Online Server (Patch 5187)

سورس سيرفر **MoonQueen** النظيف لإصدار **Conquer Online Patch 5187** (عصر النينجا الكلاسيكي)، مبني من الصفر على نواة **Comet 5187** الخام بـ C# (`.NET 6.0`) ومُجهّز بالكامل للعمل المباشر مع **XAMPP MySQL** و **Hamachi**.

---

## المميزات الجاهزة في النسخة التأسيسية (Clean Base)
- **اسم السورس والسيرفر الموحّد:** `MoonQueen` (`MoonQueen.sln`).
- **قاعدة بيانات جاهزة لـ XAMPP بضغطة واحدة:** ملف `sql/MoonQueen_Full_Setup.sql` ينشئ قاعدتي `moonqueen_account` و `moonqueen_game` تلقائياً مع حساب أدمن جاهز (`admin` / `123456`).
- **مُعدّ مسبقاً على آي بي الهاماشي:** `25.21.212.130` (بورت الحسابات `9960`، بورت اللعبة `5816`، وبورت RPC داخلي مؤمّن `127.0.0.1:5817`).
- **دخول كامل لإصدار 5187 بدون أخطاء:**
  - تشفير `RC5 + ScanCode` لحزم الحسابات، وتبادل مفاتيح `Diffie-Hellman + Blowfish CFB64` لسيرفر اللعبة.
  - متوافق 100% مع `ConquerLoader` (`COServer.dat` 32-bit Token) ومع كافة أوفسيتات باكتات `5187` (`MsgConnectEx`, `MsgConnect`, `MsgRegister`, `MsgUserInfo`, `MsgMapInfo`, `MsgAction 10010`, `MsgWalk 10005`, `MsgTalk 1004`).
  - إنشاء شخصية جديدة بجميع التخصصات الخمسة (`Trojan`, `Warrior`, `Archer`, `Ninja`, `Taoist`) والنزول في منتصف **Twin City (`1002: 430, 380`)** مع مشي وقفز وشات شغالين فوراً.

---

## التشغيل السريع (Quick Start)
1. افتح **XAMPP Control Panel** وشغّل **Apache** و **MySQL** (Port `3306`).
2. افتح `http://localhost/phpmyadmin` ⬅ اضغط **Import (استيراد)** ⬅ اختر الملف `sql/MoonQueen_Full_Setup.sql` واضغط **Go**.
3. افتح **`MoonQueen.sln`** في **Visual Studio 2022** واضغط `Build -> Rebuild Solution`.
4. شغّل **`MoonQueen.Game`** أولاً، ثم شغّل **`MoonQueen.Account`**.
5. افتح اللعبة عبر `ConquerLoader` على الآي بي `25.21.212.130` وبورت `9960` واسم السيرفر `MoonQueen`، وسجّل دخولك بـ:
   - **Username:** `admin`
   - **Password:** `123456`

لشرح تفصيلي مبسّط خطوة بخطوة راجع مجلد **[`قسم معلومات التعديلات/`](./قسم%20معلومات%20التعديلات/)**.

---

## الترخيص وحقوق النواة الأساسية
مبني على نواة [Spirited's Comet (5187)](https://github.com/conquer-online/comet/tree/5187) وفق ترخيص MIT المرفق في ملف `License`.
