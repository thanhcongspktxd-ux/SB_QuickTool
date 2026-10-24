using Autodesk.AutoCAD.ApplicationServices;   // Thư viện làm việc với Document (file CAD)
using Autodesk.AutoCAD.DatabaseServices;      // Làm việc với Database CAD
using Autodesk.AutoCAD.EditorInput;           // Nhận input từ người dùng (click, chọn)
using Autodesk.AutoCAD.Geometry;              // Point, Vector, Matrix
using Autodesk.AutoCAD.PlottingServices;      // In ấn (chưa dùng)
using Autodesk.AutoCAD.Runtime;               // Dùng để tạo Command
using Autodesk.AutoCAD.Windows;
using System;                                 // Thư viện cơ bản C#
using System.Collections.Generic;             // Dùng List, HashSet,...
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Drawing;

namespace SB_QuickTool
{
    public class Commands   // Class chứa các lệnh AutoCAD
    {
        // ========================= COMMAND 1 =========================
        [CommandMethod("VPALIGN")] // Gõ VPALIGN trong CAD sẽ chạy hàm này
        public void SetSheet()     // Method chính
        {
            try
            {
                // ===== KHAI BÁO BIẾN (OBJECT CHÍNH) =====
                Document doc = Application.DocumentManager.MdiActiveDocument; // Lấy file CAD hiện tại
                Editor ed = doc.Editor;     // Dùng để giao tiếp với user (command line)
                Database db = doc.Database; // Database chứa toàn bộ dữ liệu CAD

                // ===== LÀM VIỆC VỚI LAYOUT =====
                LayoutManager lm = LayoutManager.Current; // Lấy layout manager
                string currentLayout = lm.CurrentLayout;  // Lưu tên layout hiện tại

                // ===== STEP 1: CHỌN VÙNG =====
                PromptPointResult p1 = ed.GetPoint("\nSelect first corner: "); // User click điểm đầu
                if (p1.Status != PromptStatus.OK) return; // Nếu ESC thì thoát

                PromptPointResult p2 = ed.GetCorner("\nSelect opposite corner: ", p1.Value); // Điểm thứ 2
                if (p2.Status != PromptStatus.OK) return;

                // ===== SELECT OBJECT TRONG WINDOW =====
                PromptSelectionResult selRes = ed.SelectWindow(p1.Value, p2.Value); // Gọi method select
                if (selRes.Status != PromptStatus.OK) return;

                // ===== TẠO COLLECTION LƯU VIEWPORT =====
                HashSet<ObjectId> viewportIds = new HashSet<ObjectId>(); // HashSet không trùng

                // ===== DUYỆT QUA OBJECT ĐÃ CHỌN =====
                foreach (SelectedObject so in selRes.Value) // Loop qua selection
                {
                    if (so != null)                         // Kiểm tra null
                        viewportIds.Add(so.ObjectId);       // Thêm vào danh sách
                }

                // ===== GỌI LỆNH CAD: ZOOM WINDOW =====
                ed.Command("_.ZOOM", "_W", p1.Value, p2.Value);

                // ===== CHUYỂN SANG MODEL =====
                lm.CurrentLayout = "Model"; // Gán giá trị -> chuyển tab
                ed.Command("_.ZOOM", "_E"); // Zoom extents

                // ===== CHỌN 2 ĐIỂM CHUẨN =====
                PromptPointResult pARes = ed.GetPoint("\nPick reference point A: "); // Gọi method lấy điểm
                if (pARes.Status != PromptStatus.OK) return;

                Point3d pA = pARes.Value; // GÁN biến

                PromptPointResult pBRes = ed.GetPoint("\nPick reference point B: ");
                if (pBRes.Status != PromptStatus.OK) return;

                Point3d pB = pBRes.Value;

                // ===== QUAY LẠI LAYOUT =====
                lm.CurrentLayout = currentLayout; // Gán lại layout cũ
                ed.Command("_.ZOOM", "_E");

                // ===== BẮT ĐẦU TRANSACTION =====
                using (Transaction tr = db.TransactionManager.StartTransaction()) // Gọi method tạo transaction
                {
                    // ===== LẤY LAYOUT HIỆN TẠI =====
                    ObjectId layoutId = lm.GetLayoutId(currentLayout); // Lấy ID

                    Layout layout =
                        (Layout)tr.GetObject(layoutId, OpenMode.ForRead);

                    // ===== LẤY DANH SÁCH OBJECT TRONG LAYOUT =====
                    BlockTableRecord btr =
                        (BlockTableRecord)tr.GetObject(
                            layout.BlockTableRecordId,
                            OpenMode.ForRead
                        );

                    int movedCount = 0; // Biến đếm số viewport đã xử lý

                    // ===== DUYỆT TẤT CẢ OBJECT =====
                    foreach (ObjectId id in btr)
                    {
                        // ===== ÉP KIỂU SANG VIEWPORT =====
                        Viewport vp =
                            tr.GetObject(id, OpenMode.ForWrite) as Viewport;

                        // ===== BỎ QUA OBJECT KHÔNG HỢP LỆ =====
                        if (vp == null || vp.Number == 1)
                            continue;

                        // ===== CHỈ XỬ LÝ VIEWPORT ĐÃ CHỌN =====
                        if (!viewportIds.Contains(vp.ObjectId))
                            continue;

                        // ===== TẠO MA TRẬN CHUYỂN ĐỔI HỆ TỌA ĐỘ =====
                        Matrix3d wcsToDcs =
                            Matrix3d.PlaneToWorld(vp.ViewDirection) *
                            Matrix3d.Displacement(
                                vp.ViewTarget - Point3d.Origin
                            );

                        wcsToDcs = wcsToDcs.Inverse();

                        // ===== CHUYỂN ĐIỂM A, B SANG HỆ VIEWPORT =====
                        Point3d pA_Dcs = pA.TransformBy(wcsToDcs);
                        Point3d pB_Dcs = pB.TransformBy(wcsToDcs);

                        // ===== TÍNH ĐỘ LỆCH =====
                        double deltaX = pB_Dcs.X - pA_Dcs.X;
                        double deltaY = pB_Dcs.Y - pA_Dcs.Y;

                        // ===== DI CHUYỂN VIEWPORT =====
                        vp.ViewCenter = new Point2d(
                            vp.ViewCenter.X + deltaX,
                            vp.ViewCenter.Y + deltaY
                        );

                        movedCount++;
                    }

                    tr.Commit(); // Xác nhận thay đổi database

                    // ===== IN KẾT QUẢ RA COMMAND LINE =====
                    ed.WriteMessage($"\nMoved {movedCount} viewport(s).");
                }
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog(
                    "Error in VPALIGN:\n" + ex.Message
                );
            }
        }

        // ========================= COMMAND 2 =========================
        [CommandMethod("VPOBJECT")]
        public void JumpToObjectInViewport()
        {
            try
            {
                // ===== KHAI BÁO BIẾN =====
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Editor ed = doc.Editor;
                Database db = doc.Database;

                // ===== USER CLICK TRONG LAYOUT =====
                PromptPointResult pRes =
                    ed.GetPoint("\nClick inside viewport: ");

                if (pRes.Status != PromptStatus.OK)
                    return;

                Point3d pickPoint = pRes.Value;

                ObjectId targetViewportId = ObjectId.Null;

                // ===== TÌM VIEWPORT CHỨA ĐIỂM =====
                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    BlockTable bt =
                        (BlockTable)tr.GetObject(
                            db.BlockTableId,
                            OpenMode.ForRead
                        );

                    BlockTableRecord ps =
                        (BlockTableRecord)tr.GetObject(
                            bt[BlockTableRecord.PaperSpace],
                            OpenMode.ForRead
                        );

                    foreach (ObjectId id in ps)
                    {
                        Viewport vp =
                            tr.GetObject(id, OpenMode.ForRead) as Viewport;

                        if (vp == null || vp.Number == 1)
                            continue;

                        // ===== LẤY KHUNG VIEWPORT =====
                        Extents3d ext = vp.GeometricExtents;

                        // ===== KIỂM TRA ĐIỂM NẰM TRONG VIEWPORT =====
                        if (pickPoint.X >= ext.MinPoint.X &&
                            pickPoint.X <= ext.MaxPoint.X &&
                            pickPoint.Y >= ext.MinPoint.Y &&
                            pickPoint.Y <= ext.MaxPoint.Y)
                        {
                            targetViewportId = id;
                            break;
                        }
                    }

                    if (targetViewportId == ObjectId.Null)
                    {
                        ed.WriteMessage(
                            "\nNo viewport found at the selected point."
                        );

                        return;
                    }

                    tr.Commit();
                }

                // ===== CHUYỂN VÀO MODEL TRONG VIEWPORT =====
                ed.Command("_.MSPACE");

                // ===== CHỌN ĐIỂM TRONG MODEL =====
                PromptPointResult pModel =
                    ed.GetPoint("\nPick point in model: ");

                if (pModel.Status != PromptStatus.OK)
                    return;

                Point3d modelPoint = pModel.Value;

                // ===== CHUYỂN SANG MODEL TAB =====
                LayoutManager.Current.CurrentLayout = "Model";

                // ===== ZOOM ĐẾN ĐIỂM =====
                ed.Command("_.ZOOM", "_C", modelPoint, 100);
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog(
                    "Error in VPOBJECT:\n" + ex.Message
                );
            }
        }
    }

    //==========================================
    //==========================================
    namespace MirrorTools
    {
        // ==========================================
        // TOOL PALETTE CẢI TIẾN: HƯỚNG DẪN + TÍCH HỢP XEM VIDEO + LỊCH SỬ
        // ==========================================
        public class MirrorPaletteManager
        {
            private static PaletteSet _paletteSet;
            private static MirrorMainControl _mainControl;

            public static void ShowPalette()
            {
                if (_paletteSet == null)
                {
                    _paletteSet = new PaletteSet("MMI Workflow & Video Guide", new Guid("9F7D3E2A-1B4C-4E6F-8A9B-0C1D2E3F4A5B"));
                    _mainControl = new MirrorMainControl();
                    _paletteSet.Add("MMI Panel", _mainControl);
                    _paletteSet.Dock = DockSides.Right;
                }
                _paletteSet.Visible = true;
            }

            public static void AddHistoryLog(string blockListText)
            {
                if (_mainControl != null)
                {
                    _mainControl.AppendHistory(blockListText);
                }
            }
        }

        public partial class MirrorMainControl : System.Windows.Forms.UserControl
        {
            private System.Windows.Forms.TabControl tabControl1;
            private System.Windows.Forms.TabPage tabPageGuide;
            private System.Windows.Forms.TabPage tabPageHistory;
            private System.Windows.Forms.RichTextBox txtGuide;
            private System.Windows.Forms.RichTextBox txtHistory;
            private System.Windows.Forms.Button btnClearHistory;
            private System.Windows.Forms.Button btnUnhideAll;
            private System.Windows.Forms.Button btnWatchVideo;
            private System.Windows.Forms.Panel panelGuideButtons;

            public MirrorMainControl()
            {
                InitializeComponentCustom();
            }

            private void InitializeComponentCustom()
            {
                this.Dock = System.Windows.Forms.DockStyle.Fill;

                tabControl1 = new System.Windows.Forms.TabControl();
                tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;

                // --- TAB 1: HƯỚNG DẪN & VIDEO ---
                tabPageGuide = new System.Windows.Forms.TabPage("Hướng dẫn");

                txtGuide = new System.Windows.Forms.RichTextBox();
                txtGuide.Dock = System.Windows.Forms.DockStyle.Fill;
                txtGuide.ReadOnly = true;
                txtGuide.BackColor = Color.FromArgb(30, 30, 30);
                txtGuide.BorderStyle = System.Windows.Forms.BorderStyle.None;
                txtGuide.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);

                // Container chứa các nút bấm dưới Tab Hướng dẫn
                panelGuideButtons = new System.Windows.Forms.Panel();
                panelGuideButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
                panelGuideButtons.Height = 75;

                // Nút xem Video Hướng dẫn
                btnWatchVideo = new System.Windows.Forms.Button();
                btnWatchVideo.Text = "[►] XEM VIDEO HƯỚNG DẪN (MMI)";
                btnWatchVideo.Dock = System.Windows.Forms.DockStyle.Top;
                btnWatchVideo.Height = 35;
                btnWatchVideo.BackColor = Color.FromArgb(192, 0, 0); // Màu đỏ thu hút
                btnWatchVideo.ForeColor = Color.White;
                btnWatchVideo.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
                btnWatchVideo.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
                btnWatchVideo.Click += BtnWatchVideo_Click;

                // Nút Unhide nhanh
                btnUnhideAll = new System.Windows.Forms.Button();
                btnUnhideAll.Text = "[O] UNHIDE (HIỆN TOÀN BỘ ĐỐI TƯỢNG)";
                btnUnhideAll.Dock = System.Windows.Forms.DockStyle.Bottom;
                btnUnhideAll.Height = 35;
                btnUnhideAll.BackColor = Color.FromArgb(0, 122, 204);
                btnUnhideAll.ForeColor = Color.White;
                btnUnhideAll.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
                btnUnhideAll.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
                btnUnhideAll.Click += BtnUnhideAll_Click;

                panelGuideButtons.Controls.Add(btnWatchVideo);
                panelGuideButtons.Controls.Add(btnUnhideAll);

                // Nạp nội dung có màu sắc trực quan
                BuildFormattedGuideText();

                tabPageGuide.Controls.Add(txtGuide);
                tabPageGuide.Controls.Add(panelGuideButtons);

                // --- TAB 2: LỊCH SỬ (HISTORY LOG) ---
                tabPageHistory = new System.Windows.Forms.TabPage("Lịch sử");

                txtHistory = new System.Windows.Forms.RichTextBox();
                txtHistory.Dock = System.Windows.Forms.DockStyle.Fill;
                txtHistory.ReadOnly = true;
                txtHistory.BackColor = Color.FromArgb(25, 25, 25);
                txtHistory.ForeColor = Color.LightGreen;
                txtHistory.BorderStyle = System.Windows.Forms.BorderStyle.None;
                txtHistory.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);

                btnClearHistory = new System.Windows.Forms.Button();
                btnClearHistory.Text = "[X] Xóa nhật ký lịch sử";
                btnClearHistory.Dock = System.Windows.Forms.DockStyle.Bottom;
                btnClearHistory.Height = 30;
                btnClearHistory.BackColor = Color.FromArgb(60, 60, 60);
                btnClearHistory.ForeColor = Color.White;
                btnClearHistory.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
                btnClearHistory.Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
                btnClearHistory.Click += BtnClearHistory_Click;

                tabPageHistory.Controls.Add(txtHistory);
                tabPageHistory.Controls.Add(btnClearHistory);

                tabControl1.TabPages.Add(tabPageGuide);
                tabControl1.TabPages.Add(tabPageHistory);

                this.Controls.Add(tabControl1);
            }

            // Tạo văn bản tô màu đẹp mắt cho Tab Guide
            private void BuildFormattedGuideText()
            {
                txtGuide.Clear();

                AppendTextColored("=== HƯỚNG DẪN THỰC HIỆN LỆNH [MMI] ===\n\n", Color.Cyan, true, 11F);

                AppendTextColored("▶ BƯỚC 1: ", Color.Yellow, true, 10F);
                AppendTextColored("Kiểm tra các block (lớp ngoài cùng) đã tập trung basepoint về một điểm chưa (nếu chưa dùng lệnh BR hoặc nhờ Công hỗ trợ).\n\n", Color.White, false, 9.5F);

                AppendTextColored("▶ BƯỚC 2: ", Color.Yellow, true, 10F);
                AppendTextColored("Gõ lệnh MMI -> Chọn đối tượng -> Chọn trục mirror. Các block được chọn sẽ tự động được HIDDEN.\n\n", Color.White, false, 9.5F);

                AppendTextColored("▶ BƯỚC 3: ", Color.Yellow, true, 10F);
                AppendTextColored("Sau khi hoàn thành, nhấn nút [UNHIDE] bên dưới, kiểm tra lại dim, notes và lỗi phát sinh.\n\n", Color.White, false, 9.5F);

                AppendTextColored("----------------------------------------\n", Color.Gray, false, 9F);
                AppendTextColored("[!] CÁC CHÚ Ý QUAN TRỌNG:\n\n", Color.OrangeRed, true, 10.5F);

                AppendTextColored("1. Block [CL]:\n", Color.Orange, true, 9.5F);
                AppendTextColored("   • Nhà 1 tầng: ", Color.LightGray, true, 9F);
                AppendTextColored("Dùng tool bình thường.\n", Color.White, false, 9F);
                AppendTextColored("   • Nhà 2 tầng: ", Color.LightGray, true, 9F);
                AppendTextColored("BẮT BUỘC chọn mirror 2 tầng CÙNG LÚC để không bị lỗi.\n", Color.Magenta, true, 9.5F);
                AppendTextColored("   • Giới hạn: Chỉ mirror 1 lần/phiên để tránh trùng.\n\n", Color.LightGray, false, 9F);

                AppendTextColored("2. Block ngoại lệ:\n", Color.Orange, true, 9.5F);
                AppendTextColored("   • Các block FAE hướng (Front/Rear/Left/Right) & Roof Slope được xử lý riêng.\n\n", Color.White, false, 9F);

                AppendTextColored("3. Thông báo:\n", Color.Orange, true, 9.5F);
                AppendTextColored("   • Sau khi chạy xong sẽ hiện Pop-up & lưu lịch sử tên các block đã Hide.", Color.White, false, 9F);
            }

            private void AppendTextColored(string text, Color color, bool isBold, float size)
            {
                txtGuide.SelectionStart = txtGuide.TextLength;
                txtGuide.SelectionLength = 0;
                txtGuide.SelectionColor = color;
                txtGuide.SelectionFont = new System.Drawing.Font("Segoe UI", size, isBold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
                txtGuide.AppendText(text);
                txtGuide.SelectionColor = txtGuide.ForeColor;
            }

            public void AppendHistory(string blockListText)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss (dd/MM)");
                string logEntry = $"[{timestamp}]\n▶ Block đã Mirror & Hidden:\n{blockListText}\n----------------------------------------\n";
                txtHistory.AppendText(logEntry);
                txtHistory.SelectionStart = txtHistory.Text.Length;
                txtHistory.ScrollToCaret();
            }

            private void BtnClearHistory_Click(object sender, EventArgs e)
            {
                txtHistory.Clear();
            }

            private void BtnUnhideAll_Click(object sender, EventArgs e)
            {
                try
                {
                    Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                    {
                        doc.SendStringToExecute("_.UNISOLATEOBJECTS ", true, false, false);
                    }
                }
                catch (System.Exception ex)
                {
                    Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog("Lỗi Unhide: " + ex.Message);
                }
            }

            // Sự kiện Mở Video Hướng dẫn
            private void BtnWatchVideo_Click(object sender, EventArgs e)
            {
                try
                {
                    string userDownloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    string videoPath = Path.Combine(userDownloadsPath, "MMI(Mirror Block).mp4");

                    if (File.Exists(videoPath))
                    {
                        Process.Start(new ProcessStartInfo(videoPath) { UseShellExecute = true });
                    }
                    else
                    {
                        string relativePath = @"C:\Users\Admin\Desktop\Tool Video\MMI.mp4";
                        if (File.Exists(relativePath))
                        {
                            Process.Start(new ProcessStartInfo(relativePath) { UseShellExecute = true });
                        }
                        else
                        {
                            Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog(
                                "Không tìm thấy file video hướng dẫn!\n\nVui lòng đảm bảo file [MMI(Mirror Block).mp4] nằm trong thư mục Downloads:\n" + videoPath);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog("Không thể mở video:\n" + ex.Message);
                }
            }
        }

        public class MirrorCommands
        {
            private static int _clMirrorCount = 0;

            [CommandMethod("MMI")]
            public void MirrorSmartFinal()
            {
                try
                {
                    MirrorPaletteManager.ShowPalette();

                    Document doc = Application.DocumentManager.MdiActiveDocument;
                    Database db = doc.Database;
                    Editor ed = doc.Editor;

                    PromptSelectionResult selRes = ed.GetSelection();
                    if (selRes.Status != PromptStatus.OK) return;

                    PromptPointResult p1 = ed.GetPoint("\nChọn điểm đầu trục mirror: ");
                    if (p1.Status != PromptStatus.OK) return;

                    PromptPointOptions ppo2 = new PromptPointOptions("\nChọn điểm cuối trục mirror: ");
                    ppo2.BasePoint = p1.Value;
                    ppo2.UseBasePoint = true;

                    PromptPointResult p2 = ed.GetPoint(ppo2);
                    if (p2.Status != PromptStatus.OK) return;

                    Point3d wStart = p1.Value, wEnd = p2.Value;
                    Vector3d axisVec = wStart.GetVectorTo(wEnd).GetNormal();
                    Vector3d normalVec = axisVec.GetPerpendicularVector();
                    Plane mirrorPlane = new Plane(wStart, normalVec);
                    Matrix3d matMirrorWorld = Matrix3d.Mirroring(mirrorPlane);

                    bool clWasMirroredInThisRun = false;
                    List<string> mirroredBlockNames = new List<string>();

                    using (DocumentLock loc = doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        HashSet<ObjectId> processedBtrs = new HashSet<ObjectId>();
                        HashSet<ObjectId> hideBtrs = new HashSet<ObjectId>();

                        foreach (SelectedObject so in selRes.Value)
                        {
                            Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity;
                            if (ent == null) continue;

                            if (ent is BlockReference br)
                            {
                                string effName = GetEffectiveName(br, tr);

                                if (IsSkipMirrorBlock(effName))
                                {
                                    continue;
                                }

                                if (effName.Equals("CL", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (_clMirrorCount >= 1)
                                    {
                                        ed.WriteMessage("\n[Thông báo] Block CL đã mirror trước đó -> bỏ qua.");
                                        continue;
                                    }
                                    clWasMirroredInThisRun = true;
                                }

                                string[] skipBlocks =
                                {
                                "FAE Elevation Roof Pitch",
                                "roof slope",
                                "FAE Smoke Detector CO2 Detector"
                            };

                                if (skipBlocks.Any(x => effName.Equals(x, StringComparison.OrdinalIgnoreCase)))
                                {
                                    ent.TransformBy(matMirrorWorld);
                                    mirroredBlockNames.Add(effName);
                                    continue;
                                }

                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

                                if (btr.IsFromExternalReference)
                                {
                                    ent.TransformBy(matMirrorWorld);
                                    mirroredBlockNames.Add(effName);
                                }
                                else
                                {
                                    if (!processedBtrs.Contains(br.BlockTableRecord))
                                    {
                                        MirrorInPlaceSimulation(br, wStart, wEnd, tr);
                                        processedBtrs.Add(br.BlockTableRecord);
                                        hideBtrs.Add(br.BlockTableRecord);
                                        mirroredBlockNames.Add(effName);
                                    }
                                }
                            }
                            else
                            {
                                ent.TransformBy(matMirrorWorld);
                            }
                        }

                        tr.Commit();

                        if (clWasMirroredInThisRun) _clMirrorCount++;

                        List<ObjectId> hideIds = new List<ObjectId>();

                        using (Transaction tr2 = db.TransactionManager.StartTransaction())
                        {
                            BlockTable bt = (BlockTable)tr2.GetObject(db.BlockTableId, OpenMode.ForRead);
                            BlockTableRecord ms = (BlockTableRecord)tr2.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                            foreach (ObjectId id in ms)
                            {
                                BlockReference br = tr2.GetObject(id, OpenMode.ForRead) as BlockReference;
                                if (br == null) continue;

                                if (hideBtrs.Contains(br.BlockTableRecord))
                                {
                                    hideIds.Add(id);
                                }
                            }
                            tr2.Commit();
                        }

                        if (hideIds.Count > 0)
                        {
                            ed.SetImpliedSelection(hideIds.ToArray());
                            ed.Regen();
                            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                            ed.Command("_.HIDEOBJECTS");
                            ed.SetImpliedSelection(new ObjectId[0]);
                        }
                    }

                    ed.Regen();

                    if (mirroredBlockNames.Count > 0)
                    {
                        string uniqueNames = string.Join(", ", mirroredBlockNames.Distinct());
                        MirrorPaletteManager.AddHistoryLog(uniqueNames);
                        string alertMsg = $"Các block đã Mirror:\n[{uniqueNames}]\n\n- chúng đã được Hidden.";
                        Application.ShowAlertDialog(alertMsg);
                    }
                    else
                    {
                        Application.ShowAlertDialog("Không có block nào được mirror - chúng đã được Hidden.");
                    }
                }
                catch (System.Exception ex)
                {
                    Application.ShowAlertDialog("Error in MMI:\n" + ex.Message);
                }
            }

            private bool IsSkipMirrorBlock(string name)
            {
                if (string.IsNullOrEmpty(name)) return false;

                string upper = name.ToUpper();
                string[] dirs = { "FRONT", "REAR", "LEFT", "RIGHT" };
                string[] letters = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K" };

                foreach (string letter in letters)
                {
                    foreach (string dir in dirs)
                    {
                        if (upper.Equals($"FAE_{letter}_{dir}", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                return false;
            }

            private void MirrorInPlaceSimulation(BlockReference br, Point3d wStart, Point3d wEnd, Transaction tr)
            {
                try
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
                    Matrix3d worldToLocal = br.BlockTransform.Inverse();

                    Point3d lStart = wStart.TransformBy(worldToLocal);
                    Point3d lEnd = wEnd.TransformBy(worldToLocal);

                    Vector3d lAxisVec = lStart.GetVectorTo(lEnd).GetNormal();
                    Vector3d lNormalVec = lAxisVec.GetPerpendicularVector();
                    Plane localMirrorPlane = new Plane(lStart, lNormalVec);
                    Matrix3d matMirrorLocal = Matrix3d.Mirroring(localMirrorPlane);

                    foreach (ObjectId id in btr)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent == null) continue;

                        ent.TransformBy(matMirrorLocal);

                        if (ent is BlockReference childBr)
                        {
                            if (childBr.IsDynamicBlock)
                            {
                                childBr.RecordGraphicsModified(true);
                                continue;
                            }

                            childBr.ScaleFactors = new Scale3d(
                                Math.Abs(childBr.ScaleFactors.X),
                                Math.Abs(childBr.ScaleFactors.Y),
                                Math.Abs(childBr.ScaleFactors.Z));

                            childBr.RecordGraphicsModified(true);
                            MirrorNestedBlock(childBr, tr);
                        }
                        else
                        {
                            FixAnnotation(ent);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Application.ShowAlertDialog("Error in MirrorInPlaceSimulation:\n" + ex.Message);
                }
            }

            private void MirrorNestedBlock(BlockReference br, Transaction tr)
            {
                try
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);

                    foreach (ObjectId id in btr)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent == null) continue;

                        if (ent is BlockReference childBr)
                        {
                            if (childBr.IsDynamicBlock)
                            {
                                childBr.RecordGraphicsModified(true);
                                continue;
                            }

                            childBr.ScaleFactors = new Scale3d(
                                Math.Abs(childBr.ScaleFactors.X),
                                Math.Abs(childBr.ScaleFactors.Y),
                                Math.Abs(childBr.ScaleFactors.Z));

                            childBr.RecordGraphicsModified(true);
                            MirrorNestedBlock(childBr, tr);
                        }
                        else
                        {
                            FixAnnotation(ent);
                        }
                    }
                }
                catch { }
            }

            private void FixAnnotation(Entity ent)
            {
                try
                {
                    if (ent is DBText txt)
                    {
                        if (txt.Normal.Z < 0)
                        {
                            txt.Normal = Vector3d.ZAxis;
                            txt.Rotation = -txt.Rotation;
                        }
                    }
                    else if (ent is MText mtxt)
                    {
                        if (mtxt.Normal.Z < 0)
                        {
                            mtxt.Normal = Vector3d.ZAxis;
                            mtxt.Rotation = -mtxt.Rotation;
                        }
                    }
                    else if (ent is RotatedDimension rotDim)
                    {
                        rotDim.Normal = Vector3d.ZAxis;
                        rotDim.Rotation = Math.PI - rotDim.Rotation;
                        try { rotDim.RecomputeDimensionBlock(true); } catch { }
                    }
                    else if (ent is Dimension dim)
                    {
                        dim.Normal = Vector3d.ZAxis;
                        try { dim.RecomputeDimensionBlock(true); } catch { }
                    }
                }
                catch (System.Exception ex)
                {
                    Application.ShowAlertDialog("Error in FixAnnotation:\n" + ex.Message);
                }
            }

            private string GetEffectiveName(BlockReference br, Transaction tr)
            {
                try
                {
                    if (br.IsDynamicBlock)
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                        return btr.Name;
                    }
                    return br.Name;
                }
                catch (System.Exception ex)
                {
                    Application.ShowAlertDialog("Error in GetEffectiveName:\n" + ex.Message);
                    return "";
                }
            }


            // =====================================================================
            // LỆNH GỘP: WW - Công cụ quản lý Block thông minh (Add / Remove)
            // =====================================================================
            [CommandMethod("WW")]
            public static void FastBlockModifier()
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Editor ed = doc.Editor;

                try
                {
                    PromptKeywordOptions pko = new PromptKeywordOptions("\nSelect action [Add-to-block/Remove-from-block] <Add>: ");
                    pko.Keywords.Add("Add");
                    pko.Keywords.Add("Remove");
                    pko.Keywords.Default = "Add";

                    PromptResult pkr = ed.GetKeywords(pko);
                    if (pkr.Status != PromptStatus.OK) return;

                    string action = pkr.StringResult;

                    if (action == "Add")
                    {
                        ExecuteAddObjectToBlock(doc);
                    }
                    else if (action == "Remove")
                    {
                        ExecuteRemoveObjectFromBlock(doc);
                    }
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\nError in WC command: " + ex.Message);
                }
            }

            // =====================================================================
            // HÀM XỬ LÝ 1: Thêm đối tượng vào Block (Đã khắc phục lỗi mất text Attribute và lỗi highlight)
            // =====================================================================
            private static void ExecuteAddObjectToBlock(Document doc)
            {
                Database db = doc.Database;
                Editor ed = doc.Editor;

                PromptEntityOptions peo = new PromptEntityOptions("\nSelect destination block: ");
                peo.SetRejectMessage("\nPlease select a block reference.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return;

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelect objects to add to block: ";
                PromptSelectionResult psr = ed.GetSelection(pso);
                if (psr.Status != PromptStatus.OK) return;

                PromptKeywordOptions pko = new PromptKeywordOptions("\nDelete source objects? [Yes/No] <Yes>: ");
                pko.Keywords.Add("Yes");
                pko.Keywords.Add("No");
                pko.Keywords.Default = "Yes";
                PromptResult pkr = ed.GetKeywords(pko);
                if (pkr.Status == PromptStatus.Cancel) return;
                bool deleteSource = pkr.Status != PromptStatus.OK || pkr.StringResult == "Yes";

                try
                {
                    int count = 0;
                    string blockName = string.Empty;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockReference blkRef = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (blkRef == null) return;
                        ObjectId btrId = blkRef.BlockTableRecord;
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                        if (btr.IsFromExternalReference || btr.IsFromOverlayReference || btr.IsDependent || btr.IsLayout)
                        {
                            ed.WriteMessage("\nCannot edit an external, dependent, or layout block definition.");
                            return;
                        }

                        ObjectIdCollection sourceIds = new ObjectIdCollection();
                        foreach (SelectedObject item in psr.Value)
                        {
                            if (item == null || item.ObjectId.IsNull || item.ObjectId == per.ObjectId) continue;
                            Entity ent = tr.GetObject(item.ObjectId, OpenMode.ForRead) as Entity;
                            if (ent == null || ent.IsErased) continue;
                            if (ent is AttributeDefinition || ent is AttributeReference)
                            {
                                ed.WriteMessage("\nSkipping AttributeDefinition/AttributeReference: existing attributes are preserved.");
                                continue;
                            }
                            // Do not move geometry already owned by the destination definition.
                            if (ent.OwnerId == btrId) continue;
                            sourceIds.Add(item.ObjectId);
                        }
                        if (sourceIds.Count == 0)
                        {
                            ed.WriteMessage("\nNo valid source objects selected.");
                            return;
                        }

                        Matrix3d worldToBlock = blkRef.BlockTransform.Inverse();
                        IdMapping mapping = new IdMapping();
                        db.DeepCloneObjects(sourceIds, btrId, mapping, false);

                        foreach (ObjectId sourceId in sourceIds)
                        {
                            if (!mapping.Contains(sourceId))
                                throw new InvalidOperationException("Clone mapping missing for selected object.");
                            IdPair pair = mapping[sourceId];
                            if (!pair.IsCloned || pair.Value.IsNull)
                                throw new InvalidOperationException("Unable to clone a selected object.");
                            Entity clone = tr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
                            if (clone == null) throw new InvalidOperationException("Cloned object is not an Entity.");
                            clone.TransformBy(worldToBlock);
                            count++;
                        }

                        if (deleteSource)
                        {
                            foreach (ObjectId sourceId in sourceIds)
                            {
                                Entity source = tr.GetObject(sourceId, OpenMode.ForWrite) as Entity;
                                if (source != null) source.Erase();
                            }
                        }

                        // =====================================================================
                        // CẬP NHẬT TẤT CẢ CÁC BLOCK REFERENCE ĐỂ FIX LỖI SÁNG (HIGHLIGHT) KHI CHỌN BLOCK
                        // =====================================================================
                        ObjectIdCollection refIds = btr.GetBlockReferenceIds(true, true);
                        foreach (ObjectId refId in refIds)
                        {
                            BlockReference br = tr.GetObject(refId, OpenMode.ForWrite) as BlockReference;
                            if (br != null)
                            {
                                br.RecordGraphicsModified(true);
                            }
                        }

                        blockName = btr.Name;

                        // Hoàn tất thay đổi cơ sở dữ liệu
                        tr.Commit();
                    }

                    // Yêu cầu AutoCAD cập nhật toàn bộ bản vẽ sau khi transaction đã đóng
                    doc.SendStringToExecute("_.REGENALL ", true, false, false);
                    ed.WriteMessage($"\nAdded {count} object(s) to block [{blockName}]. Existing attributes preserved.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\nWC Add failed; transaction rolled back: " + ex.Message);
                }
            }

            // =====================================================================
            // HÀM XỬ LÝ 2: Tách nhiều đối tượng ra khỏi Block (Chọn liên tục)
            // =====================================================================
            private static void ExecuteRemoveObjectFromBlock(Document doc)
            {
                Database db = doc.Database;
                Editor ed = doc.Editor;

                try
                {
                    List<Tuple<ObjectId, ObjectId>> itemsToExtract = new List<Tuple<ObjectId, ObjectId>>();
                    ObjectId blkRefId = ObjectId.Null;

                    while (true)
                    {
                        PromptNestedEntityOptions pneo = new PromptNestedEntityOptions("\nSelect object inside block to extract (Press ESC or Enter to finish): ");
                        PromptNestedEntityResult pner = ed.GetNestedEntity(pneo);

                        if (pner.Status != PromptStatus.OK)
                        {
                            break;
                        }

                        ObjectId[] containers = pner.GetContainers();
                        if (containers.Length == 0)
                        {
                            ed.WriteMessage("\nThe selected object is not inside a block reference.");
                            continue;
                        }

                        ObjectId currentBlkRefId = containers[containers.Length - 1];
                        if (blkRefId == ObjectId.Null)
                        {
                            blkRefId = currentBlkRefId;
                        }
                        else if (blkRefId != currentBlkRefId)
                        {
                            ed.WriteMessage("\nWarning: Please select objects from the same block reference.");
                            continue;
                        }

                        ObjectId targetId = pner.ObjectId;
                        if (containers.Length > 1)
                        {
                            targetId = containers[0];
                        }

                        if (!itemsToExtract.Exists(x => x.Item2 == targetId))
                        {
                            itemsToExtract.Add(new Tuple<ObjectId, ObjectId>(blkRefId, targetId));
                            ed.WriteMessage($"\nAdded 1 item to extraction queue. Total queued: {itemsToExtract.Count}");
                        }
                    }

                    if (itemsToExtract.Count == 0)
                    {
                        ed.WriteMessage("\nNo objects selected for extraction.");
                        return;
                    }

                    int successCount = 0;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockReference blkRef = tr.GetObject(blkRefId, OpenMode.ForRead) as BlockReference;
                        if (blkRef == null) return;

                        ObjectId currentSpaceId = db.CurrentSpaceId;

                        foreach (var item in itemsToExtract)
                        {
                            ObjectId targetId = item.Item2;
                            Entity innerEnt = tr.GetObject(targetId, OpenMode.ForRead) as Entity;
                            if (innerEnt == null) continue;

                            ObjectIdCollection sourceIds = new ObjectIdCollection { targetId };
                            IdMapping idMap = new IdMapping();
                            db.DeepCloneObjects(sourceIds, currentSpaceId, idMap, false);

                            if (idMap.Contains(targetId))
                            {
                                ObjectId clonedId = idMap[targetId].Value;
                                Entity clonedEnt = tr.GetObject(clonedId, OpenMode.ForWrite) as Entity;

                                if (clonedEnt != null)
                                {
                                    clonedEnt.TransformBy(blkRef.BlockTransform);
                                }
                            }

                            Entity entToErase = tr.GetObject(targetId, OpenMode.ForWrite) as Entity;
                            if (entToErase != null)
                            {
                                entToErase.Erase();
                                successCount++;
                            }
                        }

                        blkRef.UpgradeOpen();
                        blkRef.RecordGraphicsModified(true);

                        tr.Commit();
                    }

                    ed.Regen();
                    ed.WriteMessage($"\nSuccessfully extracted {successCount} object(s) out of block to Model Space.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\nWC Remove failed; transaction rolled back: " + ex.Message);
                }
            }
        }
    }

}

