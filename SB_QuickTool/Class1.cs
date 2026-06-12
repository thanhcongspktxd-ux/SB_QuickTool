using Autodesk.AutoCAD.ApplicationServices;   // Thư viện làm việc với Document (file CAD)
using Autodesk.AutoCAD.DatabaseServices;      // Làm việc với Database CAD
using Autodesk.AutoCAD.EditorInput;           // Nhận input từ người dùng (click, chọn)
using Autodesk.AutoCAD.Geometry;              // Point, Vector, Matrix
using Autodesk.AutoCAD.PlottingServices;      // In ấn (chưa dùng)
using Autodesk.AutoCAD.Runtime;               // Dùng để tạo Command
using System;                                 // Thư viện cơ bản C#
using System.Collections.Generic;             // Dùng List, HashSet,...
using System.Linq;

namespace Autocad.Project1
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


        // =========================================================
        // MMI = MIRROR SMART FINAL
        // - Mirror object thường
        // - Mirror Xref
        // - Mirror nội dung Block giống REFEDIT
        // - Fix text/dim readable
        // - Fix nested block bị ScaleX = -1
        // - Auto Hide Similar Blocks
        // =========================================================

namespace MirrorTools
    {
        public class MirrorCommands
        {
            // Lưu số lần mirror block CL
            private static int _clMirrorCount = 0;

            [CommandMethod("MMI")]
            public void MirrorSmartFinal()
            {
                try
                {
                    // Lấy document hiện tại
                    Document doc = Application.DocumentManager.MdiActiveDocument;
                    Database db = doc.Database;
                    Editor ed = doc.Editor;

                    // User chọn object
                    PromptSelectionResult selRes = ed.GetSelection();
                    if (selRes.Status != PromptStatus.OK) return;

                    // Chọn điểm đầu mirror
                    PromptPointResult p1 = ed.GetPoint("\nChọn điểm đầu trục mirror: ");
                    if (p1.Status != PromptStatus.OK) return;

                    // Chọn điểm cuối mirror
                    PromptPointOptions ppo2 = new PromptPointOptions("\nChọn điểm cuối trục mirror: ");
                    ppo2.BasePoint = p1.Value;
                    ppo2.UseBasePoint = true;

                    PromptPointResult p2 = ed.GetPoint(ppo2);
                    if (p2.Status != PromptStatus.OK) return;

                    // Tạo mirror matrix world
                    Point3d wStart = p1.Value, wEnd = p2.Value;
                    Vector3d axisVec = wStart.GetVectorTo(wEnd).GetNormal();
                    Vector3d normalVec = axisVec.GetPerpendicularVector();
                    Plane mirrorPlane = new Plane(wStart, normalVec);
                    Matrix3d matMirrorWorld = Matrix3d.Mirroring(mirrorPlane);

                    // Đánh dấu CL có mirror trong lần chạy này
                    bool clWasMirroredInThisRun = false;

                    using (DocumentLock loc = doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        // Lưu block đã xử lý
                        HashSet<ObjectId> processedBtrs = new HashSet<ObjectId>();

                        // Lưu block để hide
                        HashSet<ObjectId> hideBtrs = new HashSet<ObjectId>();

                        // Loop object được chọn
                        foreach (SelectedObject so in selRes.Value)
                        {
                            // Lấy entity
                            Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity;
                            if (ent == null) continue;

                            // Nếu là block
                            if (ent is BlockReference br)
                            {
                                // Lấy effective name
                                string effName = GetEffectiveName(br, tr);

                                // Bỏ qua các block FAE_A/B/C..._Front/Rear/Left/Right
                                if (IsSkipMirrorBlock(effName))
                                {
                                    continue;
                                }

                                // Kiểm tra block CL
                                if (effName.Equals("CL", StringComparison.OrdinalIgnoreCase))
                                {
                                    // CL đã mirror trước đó
                                    if (_clMirrorCount >= 1)
                                    {
                                        ed.WriteMessage("\n[Thông báo] Block CL đã mirror trước đó -> bỏ qua.");
                                        continue;
                                    }

                                    // Đánh dấu CL sẽ mirror
                                    clWasMirroredInThisRun = true;
                                }

                                // Danh sách block skip
                                string[] skipBlocks =
                                {
                                "FAE Elevation Roof Pitch",
                                "roof slope",
                                "FAE Smoke Detector CO2 Detector"
                            };

                                // Mirror block skip như object thường
                                if (skipBlocks.Any(x => effName.Equals(x, StringComparison.OrdinalIgnoreCase)))
                                {
                                    ent.TransformBy(matMirrorWorld);
                                    continue;
                                }

                                // Lấy block table record
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

                                // Nếu là Xref
                                if (btr.IsFromExternalReference)
                                {
                                    // Mirror nguyên khối
                                    ent.TransformBy(matMirrorWorld);
                                }

                                // Block thường
                                else
                                {
                                    // Tránh mirror trùng definition
                                    if (!processedBtrs.Contains(br.BlockTableRecord))
                                    {
                                        // Mirror nội dung block
                                        MirrorInPlaceSimulation(br, wStart, wEnd, tr);

                                        // Đánh dấu đã xử lý
                                        processedBtrs.Add(br.BlockTableRecord);

                                        // Lưu để hide
                                        hideBtrs.Add(br.BlockTableRecord);
                                    }
                                }
                            }

                            // Object thường
                            else
                            {
                                // Mirror object thường
                                ent.TransformBy(matMirrorWorld);
                            }
                        }

                        // Commit transaction
                        tr.Commit();

                        // Tăng count nếu CL được mirror
                        if (clWasMirroredInThisRun) _clMirrorCount++;

                        // Danh sách hide object
                        List<ObjectId> hideIds = new List<ObjectId>();

                        using (Transaction tr2 = db.TransactionManager.StartTransaction())
                        {
                            // Lấy block table
                            BlockTable bt = (BlockTable)tr2.GetObject(db.BlockTableId, OpenMode.ForRead);

                            // Lấy model space
                            BlockTableRecord ms = (BlockTableRecord)tr2.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                            // Loop model space
                            foreach (ObjectId id in ms)
                            {
                                // Lấy block reference
                                BlockReference br = tr2.GetObject(id, OpenMode.ForRead) as BlockReference;
                                if (br == null) continue;

                                // Nếu cùng definition
                                if (hideBtrs.Contains(br.BlockTableRecord))
                                {
                                    // Add vào hide list
                                    hideIds.Add(id);
                                }
                            }

                            // Commit hide transaction
                            tr2.Commit();
                        }

                        // Nếu có object cần hide
                        if (hideIds.Count > 0)
                        {
                            // Preselect object
                            ed.SetImpliedSelection(hideIds.ToArray());

                            // Regen màn hình
                            ed.Regen();

                            // Focus về drawing
                            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

                            // Hide object
                            ed.Command("_.HIDEOBJECTS");

                            // Clear selection
                            ed.SetImpliedSelection(new ObjectId[0]);
                        }
                    }

                    // Refresh màn hình
                    ed.Regen();
                }

                // Báo lỗi
                catch (System.Exception ex)
                {
                    Application.ShowAlertDialog("Error in MMI:\n" + ex.Message);
                }
            }

            // Kiểm tra block cần bỏ qua
            private bool IsSkipMirrorBlock(string name)
            {
                if (string.IsNullOrEmpty(name)) return false;

                string upper = name.ToUpper();

                string[] dirs = { "FRONT", "REAR", "LEFT", "RIGHT" };
                string[] letters = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" ,"K"};

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

            // Mirror nội dung block
            private void MirrorInPlaceSimulation(BlockReference br, Point3d wStart, Point3d wEnd, Transaction tr)
            {
                try
                {
                    // Lấy block definition
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);

                    // World -> local
                    Matrix3d worldToLocal = br.BlockTransform.Inverse();

                    // Chuyển mirror axis sang local
                    Point3d lStart = wStart.TransformBy(worldToLocal);
                    Point3d lEnd = wEnd.TransformBy(worldToLocal);

                    // Tạo local mirror matrix
                    Vector3d lAxisVec = lStart.GetVectorTo(lEnd).GetNormal();
                    Vector3d lNormalVec = lAxisVec.GetPerpendicularVector();
                    Plane localMirrorPlane = new Plane(lStart, lNormalVec);
                    Matrix3d matMirrorLocal = Matrix3d.Mirroring(localMirrorPlane);

                    // Loop entity trong block
                    foreach (ObjectId id in btr)
                    {
                        // Lấy entity
                        Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent == null) continue;

                        // Mirror entity
                        ent.TransformBy(matMirrorLocal);

                        // Nếu là nested block
                        if (ent is BlockReference childBr)
                        {
                            // Fix negative scale
                            childBr.ScaleFactors = new Scale3d(
                                Math.Abs(childBr.ScaleFactors.X),
                                Math.Abs(childBr.ScaleFactors.Y),
                                Math.Abs(childBr.ScaleFactors.Z));

                            // Update graphics
                            childBr.RecordGraphicsModified(true);

                            // Reset dynamic block
                            try { childBr.ResetBlock(); } catch { }

                            // Recursive nested block
                            MirrorNestedBlock(childBr, tr);
                        }

                        // Object thường
                        else
                        {
                            // Fix text / dim
                            FixAnnotation(ent);
                        }
                    }
                }

                // Báo lỗi
                catch (System.Exception ex)
                {
                    Application.ShowAlertDialog("Error in MirrorInPlaceSimulation:\n" + ex.Message);
                }
            }

            // Recursive nested block
            private void MirrorNestedBlock(BlockReference br, Transaction tr)
            {
                try
                {
                    // Lấy block definition
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);

                    // Loop entity trong block
                    foreach (ObjectId id in btr)
                    {
                        // Lấy entity
                        Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent == null) continue;

                        // Nếu là block con
                        if (ent is BlockReference childBr)
                        {
                            // Fix negative scale
                            childBr.ScaleFactors = new Scale3d(
                                Math.Abs(childBr.ScaleFactors.X),
                                Math.Abs(childBr.ScaleFactors.Y),
                                Math.Abs(childBr.ScaleFactors.Z));

                            // Update graphics
                            childBr.RecordGraphicsModified(true);

                            // Reset dynamic block
                            try { childBr.ResetBlock(); } catch { }

                            // Recursive tiếp
                            MirrorNestedBlock(childBr, tr);
                        }

                        // Object thường
                        else
                        {
                            // Fix text / dim
                            FixAnnotation(ent);
                        }
                    }
                }
                catch { }
            }

            // Fix text / dimension bị ngược
            private void FixAnnotation(Entity ent)
            {
                try
                {
                    // Fix DBText
                    if (ent is DBText txt)
                    {
                        if (txt.Normal.Z < 0)
                        {
                            txt.Normal = Vector3d.ZAxis;
                            txt.Rotation = -txt.Rotation;
                        }
                    }

                    // Fix MText
                    else if (ent is MText mtxt)
                    {
                        if (mtxt.Normal.Z < 0)
                        {
                            mtxt.Normal = Vector3d.ZAxis;
                            mtxt.Rotation = -mtxt.Rotation;
                        }
                    }

                    // Fix rotated dimension
                    else if (ent is RotatedDimension rotDim)
                    {
                        rotDim.Normal = Vector3d.ZAxis;
                        rotDim.Rotation = Math.PI - rotDim.Rotation;

                        try { rotDim.RecomputeDimensionBlock(true); } catch { }
                    }

                    // Fix dimension thường
                    else if (ent is Dimension dim)
                    {
                        dim.Normal = Vector3d.ZAxis;

                        try { dim.RecomputeDimensionBlock(true); } catch { }
                    }
                }

                // Báo lỗi
                catch (System.Exception ex)
                {
                    Application.ShowAlertDialog("Error in FixAnnotation:\n" + ex.Message);
                }
            }

            // Lấy effective name hỗ trợ dynamic block
            private string GetEffectiveName(BlockReference br, Transaction tr)
            {
                try
                {
                    // Nếu là dynamic block
                    if (br.IsDynamicBlock)
                    {
                        // Lấy dynamic block definition
                        BlockTableRecord btr =
                            (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);

                        // Trả về dynamic name
                        return btr.Name;
                    }

                    // Trả về tên block thường
                    return br.Name;
                }

                // Báo lỗi
                catch (System.Exception ex)
                {
                    Application.ShowAlertDialog("Error in GetEffectiveName:\n" + ex.Message);
                    return "";
                }
            }

        }
    }
  }
 

