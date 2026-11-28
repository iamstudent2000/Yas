# دارایی‌های محلی برای محیط Air-Gapped

این پروژه هیچ وابستگی runtime به CDN یا Google Fonts ندارد.

## فونت فارسی

فایل‌های زیر را در `wwwroot/fonts/` قرار دهید:

- `Vazirmatn-Regular.woff2`
- `Vazirmatn-SemiBold.woff2`
- `Vazirmatn-Bold.woff2`

`css/base.css` این فایل‌ها را به صورت local بارگذاری می‌کند.

## Tabler Icons

نسخه local پکیج Tabler Icons در این مسیر قرار دارد:

`wwwroot/lib/tabler-icons/`

- `wwwroot/lib/tabler-icons/tabler-icons.min.css`
- فایل‌های فونت آیکن‌ها در `wwwroot/lib/tabler-icons/fonts/`

ویرایشگر متن راهنماها (Quill) هم local است: `wwwroot/lib/quill/`.

## نکته مهم

هیچ فایل فونت یا آیکنی از اینترنت در زمان اجرای برنامه درخواست نمی‌شود. فقط فایل‌هایی که داخل `wwwroot` قرار می‌دهید استفاده خواهند شد.
