# Mystic-Realms (Patch 5187) — Conquer Online Classic Server

مشروع سيرفر **Mystic-Realms (Conquer Online — Patch 5187)** مبني من الصفر بنظافة كاملة على معمارية **Comet** الحديثة (`.NET 6` / Async Sockets / Channels / RPC)، ويستهدف إصدار **5187** الكلاسيكي المتوازن (6 شخصيات حتى الـ **Ninja**).

> 📌 **للمطورين وأي AI Agent:** اقرأ ملف **[`AGENT_HANDOFF.md`](./AGENT_HANDOFF.md)** أولاً — يحتوي على المعمارية الكاملة، القواعد الصارمة لمنع الـ Deadlocks، أرقام الباكتات الخاصة بإصدار 5187، وخارطة الطريق المحدثة خطوة بخطوة.

---

## مكونات المستودع

### 1. مشاريع السيرفر (`src/`)
* **`Comet.Account`** — سيرفر التحقق من الحسابات وتوجيه اللاعبين للسيرفر (`Port: 9960`).
* **`Comet.Game`** — سيرفر عالم اللعبة (`Game Port: 5816`, `RPC Port: 5817`).
* **`Comet.Network`** — طبقة الشبكة اللاتزامنية (`TcpServerListener`, `PacketProcessor` عبر `System.Threading.Channels`, `RPC`, وتشفير `RC5` / `ScanCode` / `Diffie-Hellman` / `Blowfish CFB64`).
* **`Comet.Core`** — الدوال المساعدة والعمليات الأساسية.
* **`Comet.Shared`** — العقود والنماذج المشتركة للـ RPC بين السيرفرين.

### 2. قواعد البيانات (`sql/`)
* **`sql/comet.account.sql`** — قاعدة بيانات الحسابات (`comet.account`).
* **`sql/comet.game.sql`** — قاعدة بيانات عالم اللعبة (`comet.game`).

### 3. أدوات الكلينت واللودر (`tools/`)
* **`tools/DragonLauncher/`** — كود اللودر الرسمي **Dragon** (C++ Win32) لتوجيه اتصال الكلينت بدون فك تشفير `Server.dat` + تحميل `Flash.ocx` محلياً على ويندوز 10/11 + منع نوافذ المتصفح المنبثقة.
* **`tools/Comet.AutoPatcher/`** — أوتوباتشر ولانشر خفيف مكتوب بـ C# لتحديث ملفات الكلينت عبر الإنترنت (مع فحص `SHA-256`) وتشغيل اللودر تلقائياً.

---

## متطلبات التشغيل على جهازك لاحقاً
1. **.NET 6.0 SDK** (أو أحدث مع دعم `net6.0`).
2. **MariaDB** أو **MySQL 8.0**.
3. **Conquer Online Client v5187** (خام بدون تعديل).
4. **Dragon Launcher** (الموجود في `tools/DragonLauncher`) أو **ConquerLoader**.
