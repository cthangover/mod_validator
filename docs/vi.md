# Validator — kiểm tra mod mà không cần chơi

Validator đọc từng mod của game đang chạy — manifest, tệp dữ liệu, kịch bản, bản vá cảnh và mã nguồn C# — rồi báo những gì hỏng hoặc đáng ngờ: tham chiếu tới một vật phẩm, cảnh hay phông nền mà không ai khai báo, lệnh kịch bản lạ, tham số sai, phụ thuộc còn thiếu. Nó chạy nền mỗi khi game khởi động và ghi báo cáo, nên lỗi gõ sẽ được phát hiện trước khi người chơi tới dòng chứa nó.

Nó kiểm tra những gì nhìn thấy được trong tệp. Nó không chơi game: một kịch bản mà Validator chấp nhận vẫn có thể chạy khác ý tác giả, còn mã tự ghép id lúc chạy thì không theo được.

## Một lỗi và cách sửa

Một dòng kịch bản cho người chơi thịt sói, với lỗi gõ trong id vật phẩm:

```scenario
scene: home_kitchen
priority: 0
---
action item.add id=food/wolf_meet count=1
end
```

Sau lần khởi động Cthangover tiếp theo, báo cáo có:

```json
{
  "severity": "error",
  "code": "REF_NOT_FOUND",
  "message": "item 'food/wolf_meet' is referenced but never declared",
  "mod": "cthangover_game",
  "file": "scenarios/doc_validator_demo.scenario",
  "line": 4,
  "suggestion": "declare item 'food/wolf_meet' or fix the reference",
  "related": null
}
```

Mod, tệp và dòng chỉ tay vào lỗi. Sửa thành `food/wolf_meat` thì vấn đề biến mất, và số lỗi trong phần tổng giảm đi một (từ 18 xuống 17 trong lần chạy này).

## Cách chạy và cách đọc báo cáo

- Tự khởi động khi vào một game có chứa nó (Cthangover kèm nó cùng bộ công cụ dành cho nhà phát triển), sau khi các mod được tải, và chạy trên một luồng nền; không chặn game.
- Mỗi lượt chạy thêm một dòng vào nhật ký: `validator: started, 33 mod(s) queued`, rồi tới tổng số lỗi, cảnh báo và thông tin.
- Báo cáo được ghi vào `user://validation/report.json` (trong thư mục dữ liệu người dùng của game). Trong đó có phần tổng, các mod đã kiểm tra cùng phiên bản và tổng kiểm tra, và danh sách vấn đề.

Mỗi vấn đề có:

- `severity` — `error` (có thứ hỏng), `warning` (có lẽ là sai sót) hoặc `info` (đáng xem qua, ví dụ một vật phẩm chẳng ai nhắc tới);
- `code` — loại vấn đề, ví dụ `REF_NOT_FOUND`, `JSON_PARSE_ERROR`, `SCHEMA_MISSING_FIELD`, `SCHEMA_UNKNOWN_PARAM`, `DECL_UNUSED`, `DYNAMIC_KEY`;
- `mod`, `file`, `line` — sửa ở đâu; `suggestion` — làm gì.

Những thông tin `DYNAMIC_KEY` nghĩa là khóa được ghép lúc chạy nên Validator không kiểm tra được; chúng không phải lỗi.

## Kiểm tra những gì

Các phép kiểm tra chạy theo ba pha:

1. **Lược đồ** — `manifest.json` có `id` và `version` dạng chuỗi; các tệp JSON đọc được.
2. **Hợp đồng** — mỗi mod khai báo gì và nhắc tới gì: thực thể của mọi loại (nhân vật, vật phẩm, nhiệm vụ, kỹ năng, công thức, hành động và các loại do lược đồ mô tả), lệnh và hành động kịch bản cùng tham số, bản vá cảnh, các thuộc tính mà mã C# đọc và ghi.
3. **Đối chiếu** — mọi tham chiếu đều có khai báo, `depends` phủ những gì mod dùng của người khác, và các khai báo không ai dùng được liệt kê ra.

Hành động kịch bản và tham số của chúng được đối chiếu với lược đồ mà các mod kèm theo (xem Schemas), theo đúng luật của bảng lệnh.

## Cài đặt

Trong thẻ cài đặt của mod này trong trình quản lý mod:

- **Enabled**, **Run on start** — tắt lượt chạy;
- **Parse C#**, **Parse GDScript** (mặc định tắt, chậm), **Parse scenarios** — đọc những nguồn nào;
- **Write report**, **Report path** — `report.json` ghi vào đâu;
- **Block mods on error** — có trong cài đặt nhưng ở phiên bản hiện tại nó không đổi gì cả: không bao giờ chặn mod.

## Khi có trục trặc

1. **Không có báo cáo.** Game bị đóng trước khi lượt chạy kết thúc (với Cthangover mất chừng nửa phút), hoặc **Write report** đang tắt.
2. **Nhiều dòng `DYNAMIC_KEY`.** Bình thường với mã tự ghép khóa; chúng chỉ là thông tin.
