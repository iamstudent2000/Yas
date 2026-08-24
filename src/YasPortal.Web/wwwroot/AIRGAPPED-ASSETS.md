# دارایی‌های محلی برای محیط Air-Gapped

این پروژه هیچ وابستگی runtime به CDN یا Google Fonts ندارد.

## فونت فارسی

فایل‌های زیر را در `wwwroot/fonts/` قرار دهید:

- `Vazirmatn-Regular.woff2`
- `Vazirmatn-SemiBold.woff2`
- `Vazirmatn-Bold.woff2`

`css/app.css` این فایل‌ها را به صورت local بارگذاری می‌کند.

## Boxicons

نسخه local پکیج Boxicons را در این مسیر قرار دهید:

`wwwroot/lib/boxicons/`

حداقل فایل مورد نیاز:

- `wwwroot/lib/boxicons/css/boxicons.min.css`
- فایل‌های فونت Boxicons که در CSS آن با مسیر `../fonts/...` ارجاع داده شده‌اند، داخل `wwwroot/lib/boxicons/fonts/`

بهتر است ساختار پوشه را مستقیماً از package رسمی Boxicons کپی کنید تا mapping آیکن‌ها کامل باقی بماند.

## نکته مهم

هیچ فایل فونت یا Boxicons از اینترنت در زمان اجرای برنامه درخواست نمی‌شود. فقط فایل‌هایی که داخل `wwwroot` قرار می‌دهید استفاده خواهند شد.
