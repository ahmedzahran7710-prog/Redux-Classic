# خطوات تغيير اسم اللعبة في الكلينت وتجهيز داتابيز المرحلة الخامسة (`Mystic-Realms` — Patch 5187)
# Client Rebranding & Phase 5 Database Setup Guide (`Mystic-Realms`)

هذا الدليل مخصص لتطبيقه على جهاز الكمبيوتر الخاص بك عندما تبدأ بتجهيز وتشغيل الكلينت حتى تحمل واجهة الكلينت وشريط العنوان وقائمة السيرفرات اسم **`Mystic-Realms`** بالكامل متطابقاً 100% مع السيرفر!

---

## 1. تحديث قاعدتي البيانات (`SQL Databases`)

عند فتح برنامج إدارة MySQL / MariaDB (مثل **HeidiSQL** أو **Navicat**):
1. نفّذ ملف **`sql/comet.account.sql`** على قاعدة بيانات `comet.account`:
   * سيقوم تلقائياً بضبط صف السيرفر في جدول `realm` باسم **`Mystic-Realms`** وبورت الجيم `5816` وبورت الـ RPC `5817`.
2. نفّذ ملف **`sql/comet.game.sql`** على قاعدة بيانات `comet.game`:
   * سيقوم بإضافة الجداول الأربعة الجديدة للمرحلة الخامسة:
     * `cq_npc` (**458 NPC** رسمي لباتش 5187 شاملة مرشدي **`MysticGuide`** في قرية الميلاد والتاون سيتي).
     * `cq_dynanpc` (الـ NPCs الديناميكية).
     * `cq_friend` (قوائم الأصدقاء).
     * `cq_enemy` (قوائم الأعداء التلقائية).

---

## 2. تغيير اسم السيرفر في شاشة تسجيل الدخول داخل الكلينت (`Server.dat`)

لكي يظهر اسم **`Mystic-Realms`** في قائمة اختيار السيرفرات داخل كلينت 5187:
> **تنبيه مهم جداً:** اسم السيرفر داخل ملف `Server.dat` في الكلينت **يجب أن يطابق بالحرف وحالة الأحرف** الاسم الموجود في جدول `realm` في قاعدة البيانات وهو:
> **`Mystic-Realms`**

### الطريقة الأسهل (إذا كان `Server.dat` نصياً أو بعد فك تشفيره بأداة `Server.dat Decrypter` لـ 5187):
اجعل بيانات السيرفر الأول داخل `Server.dat` كالتالي:
```ini
[Header]
GroupAmount=1
Group1=Mystic-Realms

[Group1]
ServerAmount=1
Server1=Mystic-Realms
Ip1=127.0.0.1
Port1=9960
ServerName1=Mystic-Realms
HintWord1=
Pic1=servericon23
```

---

## 3. تغيير اسم نافذة اللعبة العلوية والرسائل النصية في الكلينت (`ini/StrRes.ini`)

لكي يتغير عنوان شريط نافذة الويندوز من `Conquer` إلى **`Mystic-Realms`**:
1. افتح مجلد الكلينت ثم ادخل إلى مجلد **`ini/`**.
2. افتح ملف **`StrRes.ini`** بواسطة برنامج **Notepad++** أو المفكرة العادية.
3. ابحث (`Ctrl + F`) عن كلمة `Conquer` أو عن الرقم `10052=` (عنوان النافذة في كلينتات 5187):
   * ستجد سطراً مثل:
     ```ini
     10052=Conquer
     ```
   * غيّره إلى:
     ```ini
     10052=Mystic-Realms
     ```
4. يمكنك أيضاً البحث داخل نفس ملف `StrRes.ini` عن أي رسائل ترحيب ثابتة تحتوي على `Conquer Online` واستبدالها بـ **`Mystic-Realms`** ثم حفظ الملف!

---

## 4. إعداد اللودر والأوتوباتشر (`DragonLauncher` & `AutoPatcher`)

لقد قمنا بالفعل بتجهيز الإعدادات لك داخل المستودع لتعمل فوراً:
1. في **`tools/DragonLauncher/src/Dragon.Module.Connect/Dragon.Module.Connect.json`**:
   * تم ضبط `account_server` على `127.0.0.1:9960` (`enable_redirect: true`).
   * تم ضبط `game_server` على `127.0.0.1:5816` (`enable_redirect: true`) حتى يقبل الكلينت الاتصال المحلي `127.0.0.1` فوراً على نفس الجهاز بدون أي خطأ!
   * *(عند رفع السيرفر على VPS لاحقاً، فقط استبدل `127.0.0.1` بالـ IP الحقيقي للـ VPS في هذا الملف وفي جدول `realm`).*
2. في **`tools/Comet.AutoPatcher/PatcherConfig.json`**:
   * تم ضبط `"ServerName": "Mystic-Realms"` بالفعل.
