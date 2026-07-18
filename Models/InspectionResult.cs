namespace IndustrialVisionHost.Models
{
    public class InspectionResult
    {

        //是否合格
        public bool IsOK { get; set; }

        //机器可识别的判定代码
        public InspectionJudgementCode JudgementCode { get; set; }

        //操作员可直接阅读的判定原因
        public string JudgementReason { get; set; } = "尚未判定";


        //检测目标数量
        public int Count { get; set; }

        //形态学处理后发现的原始轮廓数量
        public int RawContourCount { get; set; }


        //目标面积
        public double Area { get; set; }

        //换算后的目标物理面积（平方毫米）
        public double PhysicalArea { get; set; }


        //中心坐标
        public int CenterX { get; set; }

        public int CenterY { get; set; }

        //以图像左上角为原点的物理坐标（毫米）
        public double CenterXMillimeters { get; set; }

        public double CenterYMillimeters { get; set; }

        //目标轴对齐外接矩形尺寸
        public int WidthPixels { get; set; }

        public int HeightPixels { get; set; }

        public double WidthMillimeters { get; set; }

        public double HeightMillimeters { get; set; }

        //本次视觉处理耗时（毫秒）
        public double ProcessingTimeMs { get; set; }


    }
}
