using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    internal static class RealisticCommentGenerator
    {
        private static readonly List<string> RealisticComments = new List<string>
{
            // Các câu ngắn, vui vẻ
            "vl =)))", "sml luôn", "ơ kìa", "ko tin đc", "êee =)))",
            "tr m ơi =)))", "wth", "gắt thế", "quá gắt bro", "kh hiểu sao lun",
            "sao lại ntn", "???", "bik z là s", "ơ z là s ta", "xỉuuu",
            "đỉnh của đỉnh luôn á", "t kh tin vô mắtt", "mê chữ ê kéo dài",
            "dã man lun", "ko lối thoát", "mặn quá trời", "cười muốn sặc",
            "sao k ai nói gì 😭", "cười khum kịp", "ảo thật đấy",
            "s tội z trời", "khiếp vl", "mồm cười mắt rơi lệ",

            // Các comment dài & tự nhiên hơn
            "Mình muốn rủ bạn bè chơi cùng! 🤗",
            "Có sự kiện gì hot sắp tới không ad? 🔥",
            "Shop làm việc chuẩn, khỏi lo gì luôn 👍",
            "Có game nào chơi đêm không buồn ngủ không mn? Game này nè! 🌙",
            "Có khuyến mãi gì vào cuối tuần không vậy ad? 🎉",
            "Mình thích chơi game chiến thuật, game này phù hợp không? ♟️",
            "Game có nhiều sự kiện hấp dẫn không? 🎉",
            "Game này nhìn rất bắt mắt, muốn chơi liền! 👀",
            "Nạp lần đầu qua đây, thấy ổn hơn cả trên store luôn",
            "Game có dễ làm nhiệm vụ không mn? 📝",
            "Mình mới chơi, có ai giúp team không? 🤗",
            "Nạp số lượng lớn có giảm giá không vậy ad? 💸",
            "Mình muốn biết meta game này như nào? 📊",
            "Game này có khóa IP không mn? 🔒",
            "Lên cấp 10 rồi mà vẫn chưa hiểu hết, ai có hướng dẫn chi tiết không? 📘",
            "Có ai giống mình không, cày 1 game mà mê 4 nhân vật? 😍",
            "Game có chế độ PvP không ạ? ⚔️",
            "Nạp mà bị lỗi thì ad xử lý sao ạ? ❓",
            "Nạp lần đầu có ưu đãi gì không ad? 🎁",
            "Có ai chơi rồi review nhẹ giúp mình với?",
            "Ai chơi rồi thì chia sẻ cảm nhận nhé! 👌",
            "Nạp xong nhận đủ liền, không bị trừ thiếu",
            "Tính build team từ đầu luôn, hóng cực 🤩",
            "Game có chế độ co-op không ta? 🤝",
            "Mình thích mấy game có đồ họa kiểu này 😍",
            "Game có nhiều nhân vật để lựa chọn không? 👥",
            "Nạp xong nhận đúng gói, không chênh 1 xu 🤑",
            "Có cần liên kết gì trước khi nạp không ad? 🔗",
            "Nạp nhẹ mà nhận đủ thì mê rồi! 😍",
            "Shop rep siêu nhanh, cảm thấy rất chuyên nghiệp",
            "Mình mới nạp xong, nhận liền trong 5 phút nha, uy tín lắm! ⚡",
            "Có cần đăng nhập tài khoản không khi nạp? 🔐",
            "Tối nay rủ bạn bè cùng chơi nha! 🎮",
            "Game này có nhiều chế độ chơi không? 🎮",
            "Nạp nhẹ 50k cho đỡ ghiền, ai ngờ ghiền thiệt 😆",
            "Ai chơi thử rồi chưa? Mình đang phân vân 🤔",
            "Có ai rảnh tối nay cày game chung không? 🌙",
            "Ai chơi rồi thì chia sẻ kinh nghiệm nhé! 📚",
            "Ai rủ mình vào clan với",
            "Game này tải ở đâu vậy mn?",
            "Tối qua vừa nạp, sáng thấy ad còn check tin nhắn kỹ lắm, yên tâm hẳn",
            "Ad ơi mở thêm meme contest đi, tui có kho meme đây 😆",
            "Lúc chơi quên mất deadline, game gì gây nghiện vậy trời 😭",
            "Có giftcode không mn? 🎁",
            "Mình giới thiệu cho cả bang hội cùng nạp luôn! 🤜🤛",
            "Mình test thử trước 50k, giờ full tháng luôn rồi!",
            "Có server SEA không ad?",
            "Game có chat trong game không? 💬",
            "Game kiểu này mà không viral là hơi phí á!",
            "Mình lỡ gửi nhầm ID, shop vẫn xử lý giúp rất nhanh ⚡",
            "Có cần VPN không ad? 🌐",
            "Đã nạp nhiều lần, lần nào cũng ổn áp 🙌",
            "Game này chuẩn gu mình luôn, hóng khô cả họng 🤤",
            "Nạp xong vào game nhận đủ liền, không bị trừ thiếu",
            "Có thêm tính năng skip auto nữa thì quá đỉnh ⏭️",
            "Game có voice tiếng Nhật không? 🇯🇵",
            "Mình muốn nạp cho bạn, gửi ID bạn đó được không? 🤝",
            "Game mới mà đông người chơi vậy, hóng quá 😍",
            "Tải bên CH Play có không mọi người? 📲",
            "Mình nạp 2 lần đều nhanh và không lỗi gì, cảm ơn ad nhiều!",
            "Tối có ai rảnh lập party không? 🥳",
            "Mình thích mấy game có đồ họa anime quá! 🎨",
            "Có game mobile hot nào đang khuyến mãi nạp không? 🎈",
            "Nạp nhẹ mà nhận đủ, quá đã luôn! 🎉",
            "Game mới phát hành mà đông người chơi vậy, đẹp nhỉ! 🌟",
            "Mình test thử 1 lần, giờ thành khách quen luôn rồi 🙌",
            "Ai chơi rồi thì cho mình xin chút tips với! 💡",
            "Tải APK thì có bị lỗi không ad? ⚠️",
            "Game có rank không mn? 🏆",
            "Được giới thiệu từ bạn, nay xác nhận là đáng tin 🤗",
            "Có ai bị nghiện game này như mình không? 😅",
            "Có voice tiếng Nhật không? 🇯🇵"
        };


        private static readonly List<string> EmojiTails = new List<string>
        {
            "", " 😂", " 😭", " 🫠", " 💀", " 🤡", " ✨", " 🤣", " 😏", " 👀"
        };

        private static readonly Random rand = new Random();

        public static string GenerateComment()
        {
            string baseComment = RealisticComments[rand.Next(RealisticComments.Count)];

            // 50% chance thêm emoji đuôi
            if (rand.NextDouble() < 0.5)
            {
                baseComment += EmojiTails[rand.Next(EmojiTails.Count)];
            }

            // 20% chance chèn sai chính tả nhẹ
            baseComment = ApplySlangMistakes(baseComment);

            return baseComment.Trim();
        }

        private static string ApplySlangMistakes(string text)
        {
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "không", "k" },
                { "ko", "k" },
                { "rất", "r" },
                { "gì", "j" },
                { "cái", "c" },
                { "biết", "bik" },
                { "biết không", "bik k" },
                { "luôn", "lun" },
                { "được", "đc" },
                { "sao", "s" },
                { "thế", "th" },
                { "vậy", "v" },
                { "đây", "đ" },
                { "bây giờ", "bh" },
                { "rồi", "r" },
                { "mình", "m" },
                { "mọi người", "mn" },
                { "ad", "ađ" },
                { "có", "c" },
                { "cần", "cn" },
                { "luôn luôn", "lun lun" }
            };

            foreach (var pair in replacements.OrderByDescending(p => p.Key.Length))
            {
                // Thay đúng từ, tránh thay lẫn giữa từ (ví dụ: “biết” trong “không biết”)
                string pattern = $@"\b{Regex.Escape(pair.Key)}\b";
                text = Regex.Replace(text, pattern, pair.Value, RegexOptions.IgnoreCase);
            }

            return text;
        }


        /// <summary>
        /// Parse danh sách comment thô từ fanpage thành danh sách comment sạch.
        /// </summary>
        public static List<string> GetValidComments(string rawComment)
        {
            return string.IsNullOrWhiteSpace(rawComment)
                ? new List<string>()
                : rawComment
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToList();
        }
    }
}
