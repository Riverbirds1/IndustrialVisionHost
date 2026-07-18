using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

using IndustrialVisionHost.Camera;
using IndustrialVisionHost.Commands;
using IndustrialVisionHost.Communication;
using IndustrialVisionHost.Communication.Modbus;
using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;
using IndustrialVisionHost.Utils;
using IndustrialVisionHost.Vision;
using Mat = OpenCvSharp.Mat;

namespace IndustrialVisionHost.ViewModels
{
    public sealed class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly CameraAcquisitionService acquisitionService;
        private readonly FileLogService fileLogService;
        private readonly VisionRecipeService visionRecipeService;
        private NgImageStorageService ngImageStorageService;
        private readonly InspectionHistoryService inspectionHistoryService;
        private readonly InspectionHistoryCsvExportService
            inspectionHistoryCsvExportService;
        private readonly AuditLogService? auditLogService;
        private readonly AlarmService? alarmService;
        private SystemSettings systemSettings = new SystemSettings();
        private AuthenticatedUser currentUser;
        private UserPermission currentPermissions;
        private readonly MachineStateMachine machineStateMachine;
        private readonly SimulatedPlcServer simulatedPlcServer;
        private readonly ModbusTcpServer modbusTcpServer;
        private readonly ModbusTcpClient modbusTcpClient;
        private readonly ModbusInspectionPollingService
            modbusInspectionPollingService;
        private readonly ModbusAutoReconnectService
            modbusAutoReconnectService;
        private readonly TcpPlcClient plcClient;
        private readonly PlcHeartbeatMonitor plcHeartbeatMonitor;
        private readonly PlcAutoReconnectService plcAutoReconnectService;
        private readonly ISimulationCameraControl? simulationCameraControl;
        private readonly RelayCommand openCameraCommand;
        private readonly RelayCommand stopCameraCommand;
        private readonly RelayCommand startDetectCommand;
        private readonly RelayCommand resetMachineStateCommand;
        private readonly RelayCommand emergencyStopCommand;
        private readonly RelayCommand queryHistoryCommand;
        private readonly RelayCommand firstHistoryPageCommand;
        private readonly RelayCommand previousHistoryPageCommand;
        private readonly RelayCommand nextHistoryPageCommand;
        private readonly RelayCommand lastHistoryPageCommand;
        private readonly RelayCommand exportHistoryCsvCommand;
        private readonly RelayCommand applyBatchNumberCommand;
        private readonly RelayCommand startPlcSimulatorCommand;
        private readonly RelayCommand stopPlcSimulatorCommand;
        private readonly RelayCommand connectPlcCommand;
        private readonly RelayCommand disconnectPlcCommand;
        private readonly RelayCommand sendPingCommand;
        private readonly RelayCommand triggerPlcInspectionCommand;
        private readonly RelayCommand connectModbusCommand;
        private readonly RelayCommand disconnectModbusCommand;
        private readonly RelayCommand readModbusCommand;
        private readonly RelayCommand writeModbusTestCommand;
        private readonly RelayCommand triggerModbusInspectionCommand;

        private BitmapImage? cameraImage;
        private string cameraStatus = "状态：未连接";
        private string machineStateStatus = "设备状态：Idle（空闲）";
        private string machineStateReason = "状态原因：程序启动";
        private string operationModeStatus =
            "运行模式：手动（仅允许界面按钮触发）";
        private string databaseStatus = "检测数据库：尚未初始化";
        private string historyQueryStatus = "历史查询：尚未加载";
        private string historyTotalText = "总数：0";
        private string historyOkText = "OK：0";
        private string historyNgText = "NG：0";
        private string historyPassRateText = "合格率：0.00%";
        private string batchNumberInput = string.Empty;
        private string activeBatchNumber = string.Empty;
        private string historyBatchNumber = string.Empty;
        private int historyPageNumber = 1;
        private int historyTotalPages = 1;
        private const int HistoryPageSize = 50;
        private int count;
        private int rawContourCount;
        private long frameCount;
        private double area;
        private double physicalArea;
        private double processingTimeMs;
        private int centerX;
        private int centerY;
        private double centerXMillimeters;
        private double centerYMillimeters;
        private int widthPixels;
        private int heightPixels;
        private double widthMillimeters;
        private double heightMillimeters;
        private string judgementReason = "尚未执行检测";
        private string recipeStatus = "配方：内置默认参数";
        private string recipeNameInput = "默认配方";
        private string activeRecipeName = "内置默认参数";
        private int activeRecipeRevision = 1;
        private bool recipeHasUnsavedChanges;
        private bool isApplyingRecipe;
        private string plcHost = "127.0.0.1";
        private string plcPortText = "1502";
        private string plcSimulatorStatus = "模拟 PLC：未启动";
        private string plcConnectionStatus = "PLC 连接：未连接";
        private string lastPlcMessage = "最后通信：暂无";
        private string plcHeartbeatStatus = "心跳：未启动";
        private string plcReconnectStatus = "自动重连：未启动";
        private string plcHandshakeStatus = "检测握手：空闲";
        private string modbusRegisterStatus =
            "Modbus 内存：START=0 BUSY=0 DONE=0 OK=0 NG=0";
        private string modbusTcpStatus = "Modbus TCP：未启动";
        private string modbusClientStatus = "Modbus 客户端：未连接";
        private string modbusClientData = "协议读取：暂无数据";
        private string modbusPollingStatus = "Modbus 轮询：未启动";
        private string modbusReconnectStatus = "Modbus 重连：未启动";
        private long modbusRequestCount;
        private string lastConnectedPlcHost = "127.0.0.1";
        private int lastConnectedPlcPort = 1502;
        private string lastConnectedModbusHost = "127.0.0.1";
        private int lastConnectedModbusPort = 1503;
        private bool isPlcConnecting;
        private bool isPlcRequestRunning;
        private bool isPlcHandshakeRunning;
        private bool isModbusOperationRunning;
        private bool isHistoryExporting;
        private bool disposed;
        private bool logWriteFailureReported;
        private bool auditWriteFailureReported;
        private bool alarmWriteFailureReported;
        private string activeAlarmStatus = "活动报警：0";
        private FakeCameraScenario selectedSimulationScenario =
            FakeCameraScenario.StandardSingle;
        private VisionDebugView selectedVisionDebugView =
            VisionDebugView.Annotated;
        private OperationMode selectedOperationMode = OperationMode.Manual;
        private DateTime? historyStartDate = DateTime.Today.AddDays(-6);
        private DateTime? historyEndDate = DateTime.Today;
        private InspectionResultFilter selectedHistoryResultFilter =
            InspectionResultFilter.All;
        private DateTime resultImageVisibleUntilUtc = DateTime.MinValue;

        public MainViewModel()
            : this(new AuthenticatedUser(
                0,
                "local",
                "本地调试用户",
                UserRole.Administrator,
                false))
        {
        }

        public MainViewModel(AuthenticatedUser authenticatedUser)
            : this(new FakeCamera(), authenticatedUser, null)
        {
        }

        public MainViewModel(
            AuthenticatedUser authenticatedUser,
            AuditLogService auditLogService)
            : this(new FakeCamera(), authenticatedUser, auditLogService)
        {
        }

        public MainViewModel(
            AuthenticatedUser authenticatedUser,
            AuditLogService auditLogService,
            AlarmService alarmService)
            : this(
                new FakeCamera(),
                authenticatedUser,
                auditLogService,
                alarmService)
        {
        }

        public MainViewModel(
            AuthenticatedUser authenticatedUser,
            AuditLogService auditLogService,
            AlarmService alarmService,
            SystemSettings systemSettings)
            : this(
                new FakeCamera(),
                authenticatedUser,
                auditLogService,
                alarmService,
                systemSettings)
        {
        }

        private MainViewModel(
            FakeCamera camera,
            AuthenticatedUser authenticatedUser,
            AuditLogService? auditLogService,
            AlarmService? alarmService = null,
            SystemSettings? systemSettings = null)
            : this(
                new CameraAcquisitionService(camera),
                new FileLogService(),
                new VisionRecipeService(),
                CreateNgImageStorageService(systemSettings),
                new InspectionHistoryService(),
                new SimulatedPlcServer(),
                new TcpPlcClient(),
                camera,
                authenticatedUser,
                auditLogService,
                alarmService,
                systemSettings)
        {
        }

        public MainViewModel(
            CameraAcquisitionService acquisitionService,
            FileLogService fileLogService,
            VisionRecipeService visionRecipeService,
            NgImageStorageService ngImageStorageService,
            InspectionHistoryService inspectionHistoryService,
            SimulatedPlcServer simulatedPlcServer,
            TcpPlcClient plcClient,
            ISimulationCameraControl? simulationCameraControl = null,
            AuthenticatedUser? authenticatedUser = null,
            AuditLogService? auditLogService = null,
            AlarmService? alarmService = null,
            SystemSettings? systemSettings = null)
        {
            this.acquisitionService = acquisitionService
                ?? throw new ArgumentNullException(nameof(acquisitionService));
            this.fileLogService = fileLogService
                ?? throw new ArgumentNullException(nameof(fileLogService));
            this.visionRecipeService = visionRecipeService
                ?? throw new ArgumentNullException(nameof(visionRecipeService));
            this.ngImageStorageService = ngImageStorageService
                ?? throw new ArgumentNullException(nameof(ngImageStorageService));
            this.inspectionHistoryService = inspectionHistoryService
                ?? throw new ArgumentNullException(
                    nameof(inspectionHistoryService));
            currentUser = authenticatedUser ?? new AuthenticatedUser(
                0,
                "local",
                "本地调试用户",
                UserRole.Administrator,
                false);
            currentPermissions = UserAuthorizationPolicy.GetPermissions(
                currentUser.Role);
            this.auditLogService = auditLogService;
            this.alarmService = alarmService;
            this.systemSettings = (systemSettings ?? new SystemSettings()).Copy();
            plcHost = this.systemSettings.PlcHost;
            plcPortText = this.systemSettings.PlcTextPort.ToString();
            inspectionHistoryCsvExportService =
                new InspectionHistoryCsvExportService(
                    this.inspectionHistoryService.DatabasePath);
            machineStateMachine = new MachineStateMachine();
            this.simulatedPlcServer = simulatedPlcServer
                ?? throw new ArgumentNullException(nameof(simulatedPlcServer));
            modbusTcpServer = new ModbusTcpServer(
                this.simulatedPlcServer.DataStore);
            modbusTcpClient = new ModbusTcpClient();
            modbusInspectionPollingService =
                new ModbusInspectionPollingService(
                    modbusTcpClient,
                    ExecuteModbusInspectionAsync);
            modbusAutoReconnectService =
                new ModbusAutoReconnectService(modbusTcpClient);
            this.plcClient = plcClient
                ?? throw new ArgumentNullException(nameof(plcClient));
            plcHeartbeatMonitor = new PlcHeartbeatMonitor(this.plcClient);
            plcAutoReconnectService =
                new PlcAutoReconnectService(this.plcClient);
            this.simulationCameraControl = simulationCameraControl;

            string defaultBatchNumber =
                $"BATCH-{DateTime.Now:yyyyMMdd}";
            batchNumberInput = defaultBatchNumber;
            activeBatchNumber = defaultBatchNumber;

            if (this.simulationCameraControl is not null)
            {
                this.simulationCameraControl.Scenario = selectedSimulationScenario;
            }

            this.acquisitionService.FrameReceived += OnFrameReceived;
            VisionParameters.PropertyChanged += OnVisionParametersChanged;
            this.acquisitionService.AcquisitionFailed += OnAcquisitionFailed;
            this.acquisitionService.AcquisitionStopped += OnAcquisitionStopped;
            this.acquisitionService.Reconnecting += OnReconnecting;
            this.acquisitionService.Reconnected += OnReconnected;
            machineStateMachine.StateChanged += OnMachineStateChanged;
            this.simulatedPlcServer.MessageProcessed +=
                OnSimulatedPlcMessageProcessed;
            this.simulatedPlcServer.ClientFaulted += OnSimulatedPlcClientFaulted;
            this.simulatedPlcServer.HandshakeStateChanged +=
                OnSimulatedPlcHandshakeStateChanged;
            modbusTcpServer.RequestProcessed += OnModbusRequestProcessed;
            modbusInspectionPollingService.StatusChanged +=
                OnModbusPollingStatusChanged;
            modbusInspectionPollingService.PollingFailed +=
                OnModbusPollingFailed;
            modbusAutoReconnectService.ReconnectAttempting +=
                OnModbusReconnectAttempting;
            modbusAutoReconnectService.ReconnectAttemptFailed +=
                OnModbusReconnectAttemptFailed;
            modbusAutoReconnectService.Reconnected +=
                OnModbusReconnected;
            this.plcClient.UnsolicitedMessageReceived +=
                OnPlcUnsolicitedMessageReceived;
            plcHeartbeatMonitor.HeartbeatSucceeded += OnHeartbeatSucceeded;
            plcHeartbeatMonitor.HeartbeatFailed += OnHeartbeatFailed;
            plcAutoReconnectService.ReconnectAttempting +=
                OnReconnectAttempting;
            plcAutoReconnectService.ReconnectAttemptFailed +=
                OnReconnectAttemptFailed;
            plcAutoReconnectService.Reconnected += OnPlcReconnected;

            openCameraCommand = new RelayCommand(OpenCamera, CanOpenCamera);
            stopCameraCommand = new RelayCommand(StopCamera, CanStopCamera);
            startDetectCommand = new RelayCommand(
                () => StartDetect(DetectionTriggerSource.ManualButton),
                CanStartDetect);
            resetMachineStateCommand = new RelayCommand(
                ResetMachineState,
                CanResetMachineState);
            emergencyStopCommand = new RelayCommand(
                EmergencyStop,
                () => HasPermission(UserPermission.OperateMachine) &&
                    machineStateMachine.CurrentState !=
                    MachineState.Emergency);
            queryHistoryCommand = new RelayCommand(
                () => QueryInspectionHistory(true, true),
                () => HasPermission(UserPermission.QueryHistory));
            firstHistoryPageCommand = new RelayCommand(
                () => MoveToHistoryPage(1),
                () => HasPermission(UserPermission.QueryHistory) &&
                    historyPageNumber > 1);
            previousHistoryPageCommand = new RelayCommand(
                () => MoveToHistoryPage(historyPageNumber - 1),
                () => HasPermission(UserPermission.QueryHistory) &&
                    historyPageNumber > 1);
            nextHistoryPageCommand = new RelayCommand(
                () => MoveToHistoryPage(historyPageNumber + 1),
                () => HasPermission(UserPermission.QueryHistory) &&
                    historyPageNumber < historyTotalPages);
            lastHistoryPageCommand = new RelayCommand(
                () => MoveToHistoryPage(historyTotalPages),
                () => HasPermission(UserPermission.QueryHistory) &&
                    historyPageNumber < historyTotalPages);
            exportHistoryCsvCommand = new RelayCommand(
                ExportHistoryCsv,
                () => HasPermission(UserPermission.ExportHistory) &&
                    !isHistoryExporting);
            applyBatchNumberCommand = new RelayCommand(
                ApplyBatchNumber,
                CanApplyBatchNumber);
            OpenCameraCommand = openCameraCommand;
            StopCameraCommand = stopCameraCommand;
            StartDetectCommand = startDetectCommand;
            ResetMachineStateCommand = resetMachineStateCommand;
            EmergencyStopCommand = emergencyStopCommand;
            QueryHistoryCommand = queryHistoryCommand;
            FirstHistoryPageCommand = firstHistoryPageCommand;
            PreviousHistoryPageCommand = previousHistoryPageCommand;
            NextHistoryPageCommand = nextHistoryPageCommand;
            LastHistoryPageCommand = lastHistoryPageCommand;
            ExportHistoryCsvCommand = exportHistoryCsvCommand;
            ApplyBatchNumberCommand = applyBatchNumberCommand;
            SaveRecipeCommand = new RelayCommand(
                SaveRecipe,
                () => HasPermission(UserPermission.SaveRecipe));
            LoadRecipeCommand = new RelayCommand(
                () => LoadRecipe(false),
                () => HasPermission(UserPermission.LoadRecipe));
            startPlcSimulatorCommand = new RelayCommand(
                StartPlcSimulator,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    !this.simulatedPlcServer.IsRunning);
            stopPlcSimulatorCommand = new RelayCommand(
                StopPlcSimulator,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    this.simulatedPlcServer.IsRunning);
            connectPlcCommand = new RelayCommand(
                ConnectPlc,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    !this.plcClient.IsConnected &&
                    !isPlcConnecting &&
                    !plcAutoReconnectService.IsRunning);
            disconnectPlcCommand = new RelayCommand(
                DisconnectPlc,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    (this.plcClient.IsConnected ||
                     isPlcConnecting ||
                     plcAutoReconnectService.IsRunning));
            sendPingCommand = new RelayCommand(
                SendPing,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    this.plcClient.IsConnected && !isPlcRequestRunning);
            triggerPlcInspectionCommand = new RelayCommand(
                TriggerPlcInspection,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    this.simulatedPlcServer.IsRunning &&
                    this.plcClient.IsConnected &&
                    !isPlcHandshakeRunning &&
                    CanAcceptAutomaticDetection());
            connectModbusCommand = new RelayCommand(
                ConnectModbus,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    !modbusTcpClient.IsConnected &&
                    !isModbusOperationRunning &&
                    !modbusAutoReconnectService.IsRunning);
            disconnectModbusCommand = new RelayCommand(
                DisconnectModbus,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    (modbusTcpClient.IsConnected ||
                    modbusAutoReconnectService.IsRunning) &&
                    !isModbusOperationRunning);
            readModbusCommand = new RelayCommand(
                ReadModbus,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    modbusTcpClient.IsConnected &&
                    !isModbusOperationRunning);
            writeModbusTestCommand = new RelayCommand(
                WriteModbusTest,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    modbusTcpClient.IsConnected &&
                    !isModbusOperationRunning);
            triggerModbusInspectionCommand = new RelayCommand(
                TriggerModbusInspection,
                () => HasPermission(UserPermission.ConfigureCommunication) &&
                    simulatedPlcServer.IsRunning &&
                    modbusTcpClient.IsConnected &&
                    modbusInspectionPollingService.IsRunning &&
                    CanAcceptAutomaticDetection());
            StartPlcSimulatorCommand = startPlcSimulatorCommand;
            StopPlcSimulatorCommand = stopPlcSimulatorCommand;
            ConnectPlcCommand = connectPlcCommand;
            DisconnectPlcCommand = disconnectPlcCommand;
            SendPingCommand = sendPingCommand;
            TriggerPlcInspectionCommand = triggerPlcInspectionCommand;
            ConnectModbusCommand = connectModbusCommand;
            DisconnectModbusCommand = disconnectModbusCommand;
            ReadModbusCommand = readModbusCommand;
            WriteModbusTestCommand = writeModbusTestCommand;
            TriggerModbusInspectionCommand = triggerModbusInspectionCommand;

            AddLog(LogLevel.Info, "程序已启动，等待打开相机。");
            AddLog(
                LogLevel.Success,
                $"用户已登录：{currentUser.DisplayName}（" +
                $"{currentUser.RoleDisplayName}）。");
            AddLog(LogLevel.Info, $"日志目录：{this.fileLogService.LogDirectory}");
            AddLog(
                LogLevel.Info,
                $"NG 图片目录：{this.ngImageStorageService.ImageDirectory}");
            InitializeInspectionDatabase();
            RefreshActiveAlarmCount();
            CleanupNgImages();
            LoadRecipe(true);
        }

        public ICommand OpenCameraCommand { get; }

        public string CurrentUserStatus =>
            $"当前用户：{currentUser.DisplayName}（" +
            $"{currentUser.RoleDisplayName} / {currentUser.Username}）";

        public string PasswordSecurityNotice => currentUser.MustChangePassword
            ? "当前为初始密码，请尽快修改"
            : string.Empty;

        public string PermissionStatus => currentUser.Role switch
        {
            UserRole.Operator => "权限：生产操作与历史查看",
            UserRole.Engineer => "权限：调参、配方、通信与报表",
            UserRole.Administrator => "权限：系统全部功能",
            _ => "权限：无"
        };

        public bool CanEditVisionParameters =>
            HasPermission(UserPermission.EditVisionParameters);

        public bool CanConfigureCommunication =>
            HasPermission(UserPermission.ConfigureCommunication);

        public bool CanConfigureSimulation =>
            HasPermission(UserPermission.EditVisionParameters);

        public bool CanEditRecipeName =>
            HasPermission(UserPermission.SaveRecipe);

        public bool CanManageUsers =>
            HasPermission(UserPermission.ManageUsers);

        public bool CanManageSystemSettings =>
            HasPermission(UserPermission.ManageSystemSettings);

        public void ApplySystemSettings(SystemSettings settings)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            SystemSettingsService.Validate(settings);
            systemSettings = settings.Copy();
            PlcHost = systemSettings.PlcHost.Trim();
            PlcPortText = systemSettings.PlcTextPort.ToString();
            ngImageStorageService = CreateNgImageStorageService(systemSettings);
            OnPropertyChanged(nameof(ImageStorageStatus));
            CleanupNgImages();
            AddLog(
                LogLevel.Success,
                $"系统设置已应用：PLC {PlcHost}:{PlcPortText}，" +
                $"Modbus {systemSettings.PlcTextPort + 1}，" +
                $"NG 图片保留 {systemSettings.NgImageRetentionDays} 天，" +
                $"上限 {systemSettings.NgImageMaximumMegabytes} MB。");
        }

        public string ActiveAlarmStatus
        {
            get => activeAlarmStatus;
            private set => SetProperty(ref activeAlarmStatus, value);
        }

        public void UpdatePasswordChangedSession(
            AuthenticatedUser updatedUser)
        {
            if (updatedUser is null)
            {
                throw new ArgumentNullException(nameof(updatedUser));
            }

            if (updatedUser.Id != currentUser.Id ||
                updatedUser.Role != currentUser.Role)
            {
                throw new InvalidOperationException(
                    "密码更新后的用户会话与当前登录用户不一致。");
            }

            currentUser = updatedUser;
            currentPermissions = UserAuthorizationPolicy.GetPermissions(
                updatedUser.Role);
            OnPropertyChanged(nameof(CurrentUserStatus));
            OnPropertyChanged(nameof(PasswordSecurityNotice));
            OnPropertyChanged(nameof(PermissionStatus));
            AddLog(
                LogLevel.Success,
                $"用户 {updatedUser.Username} 已修改登录密码。" );
        }

        public ICommand StopCameraCommand { get; }

        public ICommand StartDetectCommand { get; }

        public ICommand ResetMachineStateCommand { get; }

        public ICommand EmergencyStopCommand { get; }

        public ICommand QueryHistoryCommand { get; }

        public ICommand FirstHistoryPageCommand { get; }

        public ICommand PreviousHistoryPageCommand { get; }

        public ICommand NextHistoryPageCommand { get; }

        public ICommand LastHistoryPageCommand { get; }

        public ICommand ExportHistoryCsvCommand { get; }

        public ICommand ApplyBatchNumberCommand { get; }

        public ICommand SaveRecipeCommand { get; }

        public ICommand LoadRecipeCommand { get; }

        public ICommand StartPlcSimulatorCommand { get; }

        public ICommand StopPlcSimulatorCommand { get; }

        public ICommand ConnectPlcCommand { get; }

        public ICommand DisconnectPlcCommand { get; }

        public ICommand SendPingCommand { get; }

        public ICommand TriggerPlcInspectionCommand { get; }

        public ICommand ConnectModbusCommand { get; }

        public ICommand DisconnectModbusCommand { get; }

        public ICommand ReadModbusCommand { get; }

        public ICommand WriteModbusTestCommand { get; }

        public ICommand TriggerModbusInspectionCommand { get; }

        public ObservableCollection<LogEntry> Logs { get; } = new();

        public ObservableCollection<InspectionHistoryRecord>
            HistoryRecords { get; } = new();

        public VisionParametersViewModel VisionParameters { get; } = new();

        public IReadOnlyList<KeyValuePair<VisionDebugView, string>>
            VisionDebugViews { get; } = new[]
            {
                new KeyValuePair<VisionDebugView, string>(
                    VisionDebugView.Annotated,
                    "检测标注图"),
                new KeyValuePair<VisionDebugView, string>(
                    VisionDebugView.Gray,
                    "灰度/高斯图"),
                new KeyValuePair<VisionDebugView, string>(
                    VisionDebugView.Binary,
                    "二值图"),
                new KeyValuePair<VisionDebugView, string>(
                    VisionDebugView.Morphology,
                    "形态学结果图")
            };

        public VisionDebugView SelectedVisionDebugView
        {
            get => selectedVisionDebugView;
            set
            {
                if (!CanConfigureSimulation)
                {
                    OnPropertyChanged();
                    return;
                }

                SetProperty(ref selectedVisionDebugView, value);
            }
        }

        public IReadOnlyList<KeyValuePair<FakeCameraScenario, string>>
            SimulationScenarios { get; } = new[]
            {
                new KeyValuePair<FakeCameraScenario, string>(
                    FakeCameraScenario.StandardSingle,
                    "标准单目标"),
                new KeyValuePair<FakeCameraScenario, string>(
                    FakeCameraScenario.DoubleTarget,
                    "双目标"),
                new KeyValuePair<FakeCameraScenario, string>(
                    FakeCameraScenario.SmallTargetNg,
                    "小目标 NG"),
                new KeyValuePair<FakeCameraScenario, string>(
                    FakeCameraScenario.MovingTarget,
                    "移动目标"),
                new KeyValuePair<FakeCameraScenario, string>(
                    FakeCameraScenario.DynamicDemo,
                    "动态综合"),
                new KeyValuePair<FakeCameraScenario, string>(
                    FakeCameraScenario.NoisyTarget,
                    "噪声目标"),
                new KeyValuePair<FakeCameraScenario, string>(
                    FakeCameraScenario.CaptureFailure,
                    "采集异常")
            };

        public IReadOnlyList<KeyValuePair<OperationMode, string>>
            OperationModes { get; } = new[]
            {
                new KeyValuePair<OperationMode, string>(
                    OperationMode.Manual,
                    "手动模式"),
                new KeyValuePair<OperationMode, string>(
                    OperationMode.Automatic,
                    "自动模式")
            };

        public IReadOnlyList<KeyValuePair<InspectionResultFilter, string>>
            HistoryResultFilters { get; } = new[]
            {
                new KeyValuePair<InspectionResultFilter, string>(
                    InspectionResultFilter.All,
                    "全部结果"),
                new KeyValuePair<InspectionResultFilter, string>(
                    InspectionResultFilter.Ok,
                    "仅 OK"),
                new KeyValuePair<InspectionResultFilter, string>(
                    InspectionResultFilter.Ng,
                    "仅 NG")
            };

        public DateTime? HistoryStartDate
        {
            get => historyStartDate;
            set
            {
                if (SetProperty(ref historyStartDate, value))
                {
                    InvalidateHistoryQuery();
                }
            }
        }

        public DateTime? HistoryEndDate
        {
            get => historyEndDate;
            set
            {
                if (SetProperty(ref historyEndDate, value))
                {
                    InvalidateHistoryQuery();
                }
            }
        }

        public InspectionResultFilter SelectedHistoryResultFilter
        {
            get => selectedHistoryResultFilter;
            set
            {
                if (SetProperty(ref selectedHistoryResultFilter, value))
                {
                    InvalidateHistoryQuery();
                }
            }
        }

        public string BatchNumberInput
        {
            get => batchNumberInput;
            set => SetProperty(ref batchNumberInput, value);
        }

        public string ActiveBatchStatus =>
            $"当前批次：{activeBatchNumber}";

        public string HistoryBatchNumber
        {
            get => historyBatchNumber;
            set
            {
                if (SetProperty(ref historyBatchNumber, value))
                {
                    InvalidateHistoryQuery();
                }
            }
        }

        public OperationMode SelectedOperationMode
        {
            get => selectedOperationMode;
            set
            {
                if (selectedOperationMode == value)
                {
                    return;
                }

                if (!EnsurePermission(
                        UserPermission.ChangeOperationMode,
                        "切换运行模式"))
                {
                    OnPropertyChanged();
                    return;
                }

                if (!CanChangeOperationMode)
                {
                    AddLog(
                        LogLevel.Warning,
                        $"当前设备状态 {machineStateMachine.CurrentState} " +
                        "不允许切换运行模式。" );
                    OnPropertyChanged();
                    return;
                }

                selectedOperationMode = value;
                OnPropertyChanged();
                OperationModeStatus = value == OperationMode.Manual
                    ? "运行模式：手动（仅允许界面按钮触发）"
                    : "运行模式：自动（仅允许 PLC/Modbus 触发）";
                AddLog(
                    LogLevel.Info,
                    $"运行模式已切换为" +
                    $"{(value == OperationMode.Manual ? "手动" : "自动")}。" );
                WriteAudit(
                    "ChangeOperationMode",
                    "Machine",
                    "OperationMode",
                    AuditOutcome.Success,
                    $"切换为 {value}");
                RefreshCameraCommands();
                RefreshPlcCommands();
                RefreshModbusCommands();
            }
        }

        public bool CanChangeOperationMode
        {
            get
            {
                return HasPermission(UserPermission.ChangeOperationMode) &&
                    OperationModePolicy.CanChangeMode(
                    machineStateMachine.CurrentState);
            }
        }

        public FakeCameraScenario SelectedSimulationScenario
        {
            get => selectedSimulationScenario;
            set
            {
                if (!CanConfigureSimulation)
                {
                    OnPropertyChanged();
                    return;
                }

                if (!SetProperty(ref selectedSimulationScenario, value))
                {
                    return;
                }

                if (simulationCameraControl is not null)
                {
                    simulationCameraControl.Scenario = value;
                    AddLog(
                        LogLevel.Info,
                        $"模拟场景已切换：{GetScenarioDisplayName(value)}。");
                    WriteAudit(
                        "ChangeSimulationScenario",
                        "VisionSimulation",
                        value.ToString(),
                        AuditOutcome.Success,
                        $"切换模拟场景为 {GetScenarioDisplayName(value)}");
                }
            }
        }

        public BitmapImage? CameraImage
        {
            get => cameraImage;
            private set => SetProperty(ref cameraImage, value);
        }

        public string CameraStatus
        {
            get => cameraStatus;
            private set => SetProperty(ref cameraStatus, value);
        }

        public string MachineStateStatus
        {
            get => machineStateStatus;
            private set => SetProperty(ref machineStateStatus, value);
        }

        public string MachineStateReason
        {
            get => machineStateReason;
            private set => SetProperty(ref machineStateReason, value);
        }

        public string OperationModeStatus
        {
            get => operationModeStatus;
            private set => SetProperty(ref operationModeStatus, value);
        }

        public string DatabaseStatus
        {
            get => databaseStatus;
            private set => SetProperty(ref databaseStatus, value);
        }

        public string HistoryQueryStatus
        {
            get => historyQueryStatus;
            private set => SetProperty(ref historyQueryStatus, value);
        }

        public string HistoryTotalText
        {
            get => historyTotalText;
            private set => SetProperty(ref historyTotalText, value);
        }

        public string HistoryOkText
        {
            get => historyOkText;
            private set => SetProperty(ref historyOkText, value);
        }

        public string HistoryNgText
        {
            get => historyNgText;
            private set => SetProperty(ref historyNgText, value);
        }

        public string HistoryPassRateText
        {
            get => historyPassRateText;
            private set => SetProperty(ref historyPassRateText, value);
        }

        public int Count
        {
            get => count;
            private set => SetProperty(ref count, value);
        }

        public int RawContourCount
        {
            get => rawContourCount;
            private set => SetProperty(ref rawContourCount, value);
        }

        public long FrameCount
        {
            get => frameCount;
            private set => SetProperty(ref frameCount, value);
        }

        public double Area
        {
            get => area;
            private set => SetProperty(ref area, value);
        }

        public double PhysicalArea
        {
            get => physicalArea;
            private set => SetProperty(ref physicalArea, value);
        }

        public double ProcessingTimeMs
        {
            get => processingTimeMs;
            private set => SetProperty(ref processingTimeMs, value);
        }

        public string Position => $"坐标：({centerX},{centerY})";

        public string PhysicalPosition =>
            $"物理坐标：({centerXMillimeters:F2}," +
            $"{centerYMillimeters:F2}) mm";

        public string PixelSize =>
            $"外接尺寸：{widthPixels} × {heightPixels} px";

        public string PhysicalSize =>
            $"物理尺寸：{widthMillimeters:F2} × " +
            $"{heightMillimeters:F2} mm";

        public string JudgementReason
        {
            get => judgementReason;
            private set => SetProperty(ref judgementReason, value);
        }

        public string RecipeStatus
        {
            get => recipeStatus;
            private set => SetProperty(ref recipeStatus, value);
        }

        public string RecipeNameInput
        {
            get => recipeNameInput;
            set => SetProperty(ref recipeNameInput, value);
        }

        public string ImageStorageStatus =>
            $"NG 图片：保留 {ngImageStorageService.RetentionDays} 天，" +
            $"上限 {ngImageStorageService.MaximumStorageMegabytes} MB";

        public string PlcHost
        {
            get => plcHost;
            set => SetProperty(ref plcHost, value);
        }

        public string PlcPortText
        {
            get => plcPortText;
            set => SetProperty(ref plcPortText, value);
        }

        public string PlcSimulatorStatus
        {
            get => plcSimulatorStatus;
            private set => SetProperty(ref plcSimulatorStatus, value);
        }

        public string PlcConnectionStatus
        {
            get => plcConnectionStatus;
            private set => SetProperty(ref plcConnectionStatus, value);
        }

        public string LastPlcMessage
        {
            get => lastPlcMessage;
            private set => SetProperty(ref lastPlcMessage, value);
        }

        public string PlcHeartbeatStatus
        {
            get => plcHeartbeatStatus;
            private set => SetProperty(ref plcHeartbeatStatus, value);
        }

        public string PlcReconnectStatus
        {
            get => plcReconnectStatus;
            private set => SetProperty(ref plcReconnectStatus, value);
        }

        public string PlcHandshakeStatus
        {
            get => plcHandshakeStatus;
            private set => SetProperty(ref plcHandshakeStatus, value);
        }

        public string ModbusRegisterStatus
        {
            get => modbusRegisterStatus;
            private set => SetProperty(ref modbusRegisterStatus, value);
        }

        public string ModbusTcpStatus
        {
            get => modbusTcpStatus;
            private set => SetProperty(ref modbusTcpStatus, value);
        }

        public string ModbusClientStatus
        {
            get => modbusClientStatus;
            private set => SetProperty(ref modbusClientStatus, value);
        }

        public string ModbusClientData
        {
            get => modbusClientData;
            private set => SetProperty(ref modbusClientData, value);
        }

        public string ModbusPollingStatus
        {
            get => modbusPollingStatus;
            private set => SetProperty(ref modbusPollingStatus, value);
        }

        public string ModbusReconnectStatus
        {
            get => modbusReconnectStatus;
            private set => SetProperty(ref modbusReconnectStatus, value);
        }

        private static NgImageStorageService CreateNgImageStorageService(
            SystemSettings? settings)
        {
            SystemSettings effectiveSettings = settings ?? new SystemSettings();
            SystemSettingsService.Validate(effectiveSettings);
            return new NgImageStorageService(
                retentionDays: effectiveSettings.NgImageRetentionDays,
                maximumStorageBytes:
                    effectiveSettings.NgImageMaximumMegabytes * 1024L * 1024L);
        }

        private bool CanOpenCamera()
        {
            return HasPermission(UserPermission.OperateMachine) &&
                !acquisitionService.IsRunning &&
                machineStateMachine.CurrentState != MachineState.Emergency;
        }

        private bool HasPermission(UserPermission permission)
        {
            return (currentPermissions & permission) == permission;
        }

        private bool EnsurePermission(
            UserPermission permission,
            string operationName)
        {
            if (HasPermission(permission))
            {
                return true;
            }

            AddLog(
                LogLevel.Warning,
                $"权限拒绝：{currentUser.DisplayName}（" +
                $"{currentUser.RoleDisplayName}）不能执行“{operationName}”。");
            WriteAudit(
                "PermissionDenied",
                "Operation",
                operationName,
                AuditOutcome.Denied,
                $"角色 {currentUser.Role} 无权执行该操作");
            return false;
        }

        private void WriteAudit(
            string actionType,
            string targetType,
            string targetIdentifier,
            AuditOutcome outcome,
            string details)
        {
            if (auditLogService is null)
            {
                return;
            }

            if (auditLogService.TryWrite(
                    currentUser,
                    actionType,
                    targetType,
                    targetIdentifier,
                    outcome,
                    details,
                    out string? errorMessage) ||
                auditWriteFailureReported)
            {
                return;
            }

            auditWriteFailureReported = true;
            AddLog(
                LogLevel.Warning,
                $"操作审计写入失败，本次业务继续执行：{errorMessage}");
        }

        private void RaiseAlarm(
            string alarmCode,
            AlarmSeverity severity,
            string source,
            string message)
        {
            if (alarmService is null)
            {
                return;
            }

            if (!alarmService.TryRaise(
                    alarmCode,
                    severity,
                    source,
                    message,
                    out long alarmId,
                    out bool isNewAlarm,
                    out string? errorMessage))
            {
                ReportAlarmStorageFailure(errorMessage);
                return;
            }

            RefreshActiveAlarmCount();
            if (!isNewAlarm)
            {
                return;
            }

            AddLog(
                severity == AlarmSeverity.Warning
                    ? LogLevel.Warning
                    : LogLevel.Error,
                $"报警发生：{alarmCode}，{message}");
            WriteAudit(
                "RaiseAlarm",
                "Alarm",
                alarmId.ToString(),
                AuditOutcome.Success,
                $"{alarmCode} / {source} / {message}");
        }

        private void ClearAlarm(
            string alarmCode,
            string source,
            string reason)
        {
            if (alarmService is null)
            {
                return;
            }

            if (!alarmService.TryClearActive(
                    alarmCode,
                    source,
                    reason,
                    out int clearedCount,
                    out string? errorMessage))
            {
                ReportAlarmStorageFailure(errorMessage);
                return;
            }

            if (clearedCount > 0)
            {
                AddLog(LogLevel.Success, $"报警恢复：{alarmCode}，{reason}");
                WriteAudit(
                    "ClearAlarm",
                    "Alarm",
                    alarmCode,
                    AuditOutcome.Success,
                    $"{source} / {reason}");
            }

            RefreshActiveAlarmCount();
        }

        public void RefreshActiveAlarmCount()
        {
            if (alarmService is null)
            {
                ActiveAlarmStatus = "活动报警：功能未启用";
                return;
            }

            if (alarmService.TryGetActiveCount(
                    out long activeCount,
                    out string? errorMessage))
            {
                ActiveAlarmStatus = $"活动报警：{activeCount}";
                return;
            }

            ActiveAlarmStatus = "活动报警：读取失败";
            ReportAlarmStorageFailure(errorMessage);
        }

        private void ReportAlarmStorageFailure(string? errorMessage)
        {
            if (alarmWriteFailureReported)
            {
                return;
            }

            alarmWriteFailureReported = true;
            AddLog(
                LogLevel.Warning,
                $"报警数据库写入失败，本次业务继续执行：{errorMessage}");
        }

        private void StartPlcSimulator()
        {
            if (!TryGetPlcEndpoint(out _, out int port, out string? errorMessage))
            {
                AddLog(LogLevel.Warning, $"模拟 PLC 启动失败：{errorMessage}");
                return;
            }

            if (port == 65535)
            {
                AddLog(
                    LogLevel.Warning,
                    "模拟 PLC 启动失败：Modbus TCP 需要使用文本端口的下一个端口，请输入不大于 65534 的端口。");
                return;
            }

            try
            {
                simulatedPlcServer.Start(port);
                modbusTcpServer.Start(port + 1);
                modbusRequestCount = 0;
                PlcSimulatorStatus =
                    $"模拟 PLC：文本协议监听 127.0.0.1:{simulatedPlcServer.ListeningPort}";
                ModbusTcpStatus =
                    $"Modbus TCP：监听 127.0.0.1:{modbusTcpServer.ListeningPort}，请求数 0";
                AddLog(
                    LogLevel.Success,
                    $"模拟 PLC 已启动：文本协议端口 {simulatedPlcServer.ListeningPort}，" +
                    $"Modbus TCP 端口 {modbusTcpServer.ListeningPort}。");
            }
            catch (Exception ex)
            {
                modbusTcpServer.Stop();
                simulatedPlcServer.Stop();
                PlcSimulatorStatus = "模拟 PLC：启动失败";
                ModbusTcpStatus = "Modbus TCP：启动失败";
                AddLog(LogLevel.Error, $"模拟 PLC 启动失败：{ex.Message}");
            }
            finally
            {
                RefreshPlcCommands();
                RefreshModbusCommands();
            }
        }

        private void StopPlcSimulator()
        {
            modbusTcpServer.Stop();
            simulatedPlcServer.Stop();
            PlcSimulatorStatus = "模拟 PLC：未启动";
            ModbusTcpStatus = "Modbus TCP：未启动";
            if (plcClient.IsConnected)
            {
                PlcConnectionStatus = "PLC 连接：等待心跳确认断线";
                AddLog(
                    LogLevel.Warning,
                    "模拟 PLC 已停止，等待上位机心跳检测远端断线。");
            }
            else
            {
                AddLog(LogLevel.Info, "模拟 PLC 已停止。");
            }
            RefreshPlcCommands();
            RefreshModbusCommands();
        }

        private async void ConnectPlc()
        {
            if (!TryGetPlcEndpoint(
                    out string host,
                    out int port,
                    out string? errorMessage))
            {
                AddLog(LogLevel.Warning, $"PLC 连接失败：{errorMessage}");
                return;
            }

            isPlcConnecting = true;
            plcAutoReconnectService.Stop();
            lastConnectedPlcHost = host;
            lastConnectedPlcPort = port;
            PlcConnectionStatus = $"PLC 连接：正在连接 {host}:{port}";
            PlcReconnectStatus = "自动重连：未启动";
            RefreshPlcCommands();

            try
            {
                await plcClient.ConnectAsync(
                    host,
                    port,
                    TimeSpan.FromSeconds(3));
                PlcConnectionStatus = $"PLC 连接：已连接 {host}:{port}";
                PlcHeartbeatStatus = "心跳：正在确认链路";
                plcHeartbeatMonitor.Start();
                AddLog(LogLevel.Success, $"PLC TCP 连接成功：{host}:{port}。");
            }
            catch (Exception ex)
            {
                PlcConnectionStatus = "PLC 连接：连接失败，进入自动重连";
                PlcReconnectStatus = "自动重连：1 秒后开始";
                AddLog(
                    LogLevel.Warning,
                    $"PLC TCP 连接失败，进入自动重连：{ex.Message}");
                plcAutoReconnectService.Start(host, port);
            }
            finally
            {
                isPlcConnecting = false;
                RefreshPlcCommands();
            }
        }

        private void DisconnectPlc()
        {
            plcAutoReconnectService.Stop();
            plcHeartbeatMonitor.Stop();
            plcClient.Disconnect();
            PlcConnectionStatus = "PLC 连接：未连接";
            PlcHeartbeatStatus = "心跳：未启动";
            PlcReconnectStatus = "自动重连：已取消";
            AddLog(LogLevel.Info, "PLC TCP 连接已主动断开。");
            RefreshPlcCommands();
        }

        private async void SendPing()
        {
            isPlcRequestRunning = true;
            LastPlcMessage = "最后通信：TX PING，等待响应";
            RefreshPlcCommands();

            try
            {
                AddLog(LogLevel.Info, "PLC TX：PING");
                string response = await plcClient.SendRequestAsync(
                    "PING",
                    TimeSpan.FromSeconds(2));
                LastPlcMessage = $"最后通信：TX PING / RX {response}";
                AddLog(LogLevel.Success, $"PLC RX：{response}");
            }
            catch (Exception ex)
            {
                PlcConnectionStatus = "PLC 连接：通信失败，连接已关闭";
                LastPlcMessage = "最后通信：PING 失败";
                AddLog(LogLevel.Error, $"PLC PING 通信失败：{ex.Message}");
            }
            finally
            {
                isPlcRequestRunning = false;
                RefreshPlcCommands();
            }
        }

        private async void TriggerPlcInspection()
        {
            try
            {
                string cycleId =
                    await simulatedPlcServer.TriggerInspectionAsync();
                PlcHandshakeStatus = $"检测握手：{cycleId} 已触发";
                AddLog(
                    LogLevel.Info,
                    $"模拟 PLC 主动发送 START {cycleId}。");
            }
            catch (Exception ex)
            {
                PlcHandshakeStatus = "检测握手：触发失败";
                AddLog(
                    LogLevel.Error,
                    $"模拟 PLC 触发检测失败：{ex.Message}");
            }
            finally
            {
                RefreshPlcCommands();
            }
        }

        private void OnPlcUnsolicitedMessageReceived(string message)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (message.StartsWith(
                        "START ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    _ = HandlePlcStartAsync(message);
                    return;
                }

                AddLog(LogLevel.Warning, $"收到未处理的 PLC 主动报文：{message}");
            });
        }

        private async Task HandlePlcStartAsync(string message)
        {
            string[] parts = message.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2 || isPlcHandshakeRunning)
            {
                AddLog(LogLevel.Warning, $"无法处理 PLC START 报文：{message}");
                return;
            }

            string cycleId = parts[1];
            isPlcHandshakeRunning = true;
            PlcHandshakeStatus = $"检测握手：{cycleId} 正在处理";
            RefreshPlcCommands();

            try
            {
                AddLog(LogLevel.Info, $"PLC RX：START {cycleId}");
                string busyAck = await plcClient.SendRequestAsync(
                    $"BUSY {cycleId}",
                    TimeSpan.FromSeconds(2));

                if (busyAck != $"ACK BUSY {cycleId}")
                {
                    throw new InvalidOperationException(
                        $"BUSY 应答无效：{busyAck}");
                }

                AddLog(LogLevel.Info, $"PLC TX：BUSY {cycleId}");
                InspectionResult? inspection = StartDetect(
                    DetectionTriggerSource.TextPlc,
                    cycleId);
                string resultCode = inspection?.IsOK == true ? "OK" : "NG";
                string resultAck = await plcClient.SendRequestAsync(
                    $"RESULT {cycleId} {resultCode}",
                    TimeSpan.FromSeconds(2));

                if (resultAck != $"ACK RESULT {cycleId}")
                {
                    throw new InvalidOperationException(
                        $"RESULT 应答无效：{resultAck}");
                }

                PlcHandshakeStatus =
                    $"检测握手：{cycleId} 完成，结果 {resultCode}";
                AddLog(
                    resultCode == "OK" ? LogLevel.Success : LogLevel.Warning,
                    $"PLC TX：RESULT {cycleId} {resultCode}，握手完成。");
            }
            catch (Exception ex)
            {
                PlcHandshakeStatus = $"检测握手：{cycleId} 失败";
                AddLog(
                    LogLevel.Error,
                    $"PLC 检测握手失败：{ex.Message}");
            }
            finally
            {
                isPlcHandshakeRunning = false;
                RefreshPlcCommands();
            }
        }

        private bool TryGetPlcEndpoint(
            out string host,
            out int port,
            out string? errorMessage)
        {
            host = PlcHost.Trim();
            port = 0;

            if (string.IsNullOrWhiteSpace(host))
            {
                errorMessage = "PLC 地址不能为空。";
                return false;
            }

            if (!int.TryParse(PlcPortText, out port) ||
                port < 1 ||
                port > 65535)
            {
                errorMessage = "PLC 端口必须是 1～65535 的整数。";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private bool TryGetModbusEndpoint(
            out string host,
            out int port,
            out string? errorMessage)
        {
            if (!TryGetPlcEndpoint(
                    out host,
                    out int textProtocolPort,
                    out errorMessage))
            {
                port = 0;
                return false;
            }

            if (textProtocolPort == 65535)
            {
                port = 0;
                errorMessage =
                    "Modbus TCP 使用文本端口的下一个端口，文本端口不能是 65535。";
                return false;
            }

            port = textProtocolPort + 1;
            return true;
        }

        private async void ConnectModbus()
        {
            if (!TryGetModbusEndpoint(
                    out string host,
                    out int port,
                    out string? errorMessage))
            {
                AddLog(
                    LogLevel.Warning,
                    $"Modbus TCP 连接失败：{errorMessage}");
                return;
            }

            isModbusOperationRunning = true;
            modbusAutoReconnectService.Stop();
            lastConnectedModbusHost = host;
            lastConnectedModbusPort = port;
            ModbusClientStatus =
                $"Modbus 客户端：正在连接 {host}:{port}";
            ModbusReconnectStatus = "Modbus 重连：未启动";
            RefreshModbusCommands();

            try
            {
                await modbusTcpClient.ConnectAsync(
                    host,
                    port,
                    TimeSpan.FromSeconds(3));
                modbusInspectionPollingService.Start();
                ModbusClientStatus =
                    $"Modbus 客户端：已连接 {host}:{port}";
                ModbusReconnectStatus = "Modbus 重连：未启动";
                AddLog(
                    LogLevel.Success,
                    $"Modbus TCP 客户端连接成功：{host}:{port}。" );
            }
            catch (Exception ex)
            {
                modbusInspectionPollingService.Stop();
                modbusTcpClient.Disconnect();
                ModbusClientStatus =
                    "Modbus 客户端：连接失败，进入自动重连";
                ModbusPollingStatus = "Modbus 轮询：未启动";
                ModbusReconnectStatus = "Modbus 重连：1 秒后开始";
                AddLog(
                    LogLevel.Warning,
                    $"Modbus TCP 客户端连接失败，进入自动重连：" +
                    ex.Message);
                modbusAutoReconnectService.Start(host, port);
            }
            finally
            {
                isModbusOperationRunning = false;
                RefreshModbusCommands();
            }
        }

        private void DisconnectModbus()
        {
            modbusAutoReconnectService.Stop();
            modbusInspectionPollingService.Stop();
            modbusTcpClient.Disconnect();
            ModbusClientStatus = "Modbus 客户端：未连接";
            ModbusPollingStatus = "Modbus 轮询：未启动";
            ModbusReconnectStatus = "Modbus 重连：已取消";
            AddLog(LogLevel.Info, "Modbus TCP 客户端已断开。");
            RefreshModbusCommands();
        }

        private async void ReadModbus()
        {
            await RunModbusOperationAsync(
                "读取",
                RefreshModbusClientDataAsync);
        }

        private async void WriteModbusTest()
        {
            await RunModbusOperationAsync(
                "测试写入",
                async () =>
                {
                    TimeSpan timeout = TimeSpan.FromSeconds(2);
                    bool[] testCoil = await modbusTcpClient.ReadCoilsAsync(
                        10,
                        1,
                        timeout);
                    ushort[] testRegister =
                        await modbusTcpClient.ReadHoldingRegistersAsync(
                            10,
                            1,
                            timeout);

                    await modbusTcpClient.WriteSingleCoilAsync(
                        10,
                        !testCoil[0],
                        timeout);
                    await modbusTcpClient.WriteSingleHoldingRegisterAsync(
                        10,
                        unchecked((ushort)(testRegister[0] + 1)),
                        timeout);
                    await RefreshModbusClientDataAsync();
                });
        }

        private void TriggerModbusInspection()
        {
            try
            {
                ushort cycleId =
                    simulatedPlcServer.TriggerModbusInspection();
                ModbusPollingStatus =
                    $"Modbus 轮询：等待处理周期 {cycleId}";
                AddLog(
                    LogLevel.Info,
                    $"模拟 PLC 已置 START=1，Modbus 周期 {cycleId}。" );
            }
            catch (Exception ex)
            {
                AddLog(
                    LogLevel.Warning,
                    $"Modbus 检测触发失败：{ex.Message}");
            }
            finally
            {
                RefreshModbusCommands();
            }
        }

        private async Task<bool> ExecuteModbusInspectionAsync(ushort cycleId)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PlcHandshakeStatus =
                    $"检测握手：Modbus 周期 {cycleId} 正在检测";
                AddLog(
                    LogLevel.Info,
                    $"Modbus 周期 {cycleId}：收到 START，已回写 BUSY。" );

                InspectionResult? inspection = StartDetect(
                    DetectionTriggerSource.Modbus,
                    cycleId.ToString());
                bool isOk = inspection?.IsOK == true;
                AddLog(
                    isOk ? LogLevel.Success : LogLevel.Warning,
                    $"Modbus 周期 {cycleId}：视觉检测结果 " +
                    $"{(isOk ? "OK" : "NG")}。" );
                return isOk;
            });
        }

        private async Task RefreshModbusClientDataAsync()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(2);
            bool[] coils = await modbusTcpClient.ReadCoilsAsync(
                0,
                11,
                timeout);
            ushort[] registers =
                await modbusTcpClient.ReadHoldingRegistersAsync(
                    0,
                    11,
                    timeout);

            ModbusClientData =
                $"协议读取：START={(coils[0] ? 1 : 0)} " +
                $"BUSY={(coils[1] ? 1 : 0)} " +
                $"DONE={(coils[2] ? 1 : 0)} " +
                $"OK={(coils[3] ? 1 : 0)} " +
                $"NG={(coils[4] ? 1 : 0)}\n" +
                $"HR0 周期={registers[0]}，HR1 结果码={registers[1]}；" +
                $"测试区 C10={(coils[10] ? 1 : 0)}，HR10={registers[10]}";
        }

        private async Task RunModbusOperationAsync(
            string operationName,
            Func<Task> operation)
        {
            isModbusOperationRunning = true;
            RefreshModbusCommands();

            try
            {
                await operation();
                ModbusClientStatus =
                    $"Modbus 客户端：{operationName}成功，{DateTime.Now:HH:mm:ss}";
                AddLog(
                    LogLevel.Success,
                    $"Modbus TCP {operationName}成功。" );
            }
            catch (Exception ex)
            {
                ModbusClientStatus = modbusTcpClient.IsConnected
                    ? $"Modbus 客户端：{operationName}失败"
                    : "Modbus 客户端：通信失败，连接已关闭";
                AddLog(
                    LogLevel.Error,
                    $"Modbus TCP {operationName}失败：{ex.Message}");
            }
            finally
            {
                isModbusOperationRunning = false;
                RefreshModbusCommands();
            }
        }

        private void RefreshPlcCommands()
        {
            startPlcSimulatorCommand.RaiseCanExecuteChanged();
            stopPlcSimulatorCommand.RaiseCanExecuteChanged();
            connectPlcCommand.RaiseCanExecuteChanged();
            disconnectPlcCommand.RaiseCanExecuteChanged();
            sendPingCommand.RaiseCanExecuteChanged();
            triggerPlcInspectionCommand.RaiseCanExecuteChanged();
        }

        private void RefreshModbusCommands()
        {
            connectModbusCommand.RaiseCanExecuteChanged();
            disconnectModbusCommand.RaiseCanExecuteChanged();
            readModbusCommand.RaiseCanExecuteChanged();
            writeModbusTestCommand.RaiseCanExecuteChanged();
            triggerModbusInspectionCommand.RaiseCanExecuteChanged();
        }

        private void OnSimulatedPlcMessageProcessed(
            string request,
            string response)
        {
            if (string.Equals(
                    request,
                    "HEARTBEAT",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = Application.Current.Dispatcher.InvokeAsync(() =>
                AddLog(
                    LogLevel.Info,
                    $"模拟 PLC：收到 {request}，应答 {response}。"));
        }

        private void OnHeartbeatSucceeded(TimeSpan elapsed)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ClearAlarm(
                    "PLC_CONNECTION_LOST",
                    "PLC.TextTcp",
                    "PLC 心跳已恢复正常");
                PlcConnectionStatus = "PLC 连接：在线";
                PlcHeartbeatStatus =
                    $"心跳：正常，{elapsed.TotalMilliseconds:F0} ms，" +
                    $"{DateTime.Now:HH:mm:ss}";
                RefreshPlcCommands();
            });
        }

        private void OnHeartbeatFailed(Exception exception)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RaiseAlarm(
                    "PLC_CONNECTION_LOST",
                    AlarmSeverity.Error,
                    "PLC.TextTcp",
                    $"PLC 心跳失败：{exception.Message}");
                PlcConnectionStatus = "PLC 连接：已断线";
                PlcHeartbeatStatus = "心跳：失败";
                LastPlcMessage = "最后通信：心跳失败";
                AddLog(
                    LogLevel.Error,
                    $"PLC 心跳失败，连接已关闭：{exception.Message}");
                PlcReconnectStatus = "自动重连：1 秒后开始";
                plcAutoReconnectService.Start(
                    lastConnectedPlcHost,
                    lastConnectedPlcPort);
                RefreshPlcCommands();
            });
        }

        private void OnReconnectAttempting(int attempt)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PlcConnectionStatus =
                    $"PLC 连接：正在执行第 {attempt} 次重连";
                PlcReconnectStatus = $"自动重连：第 {attempt} 次尝试";
                RefreshPlcCommands();
            });
        }

        private void OnReconnectAttemptFailed(
            int attempt,
            Exception exception,
            TimeSpan nextDelay)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PlcConnectionStatus = "PLC 连接：仍处于断线状态";
                PlcReconnectStatus =
                    $"自动重连：第 {attempt} 次失败，" +
                    $"{nextDelay.TotalSeconds:F0} 秒后重试";

                if (attempt == 1 || attempt % 10 == 0)
                {
                    AddLog(
                        LogLevel.Warning,
                        $"PLC 第 {attempt} 次自动重连失败：" +
                        $"{exception.Message}");
                }

                RefreshPlcCommands();
            });
        }

        private void OnPlcReconnected(int attempt)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ClearAlarm(
                    "PLC_CONNECTION_LOST",
                    "PLC.TextTcp",
                    $"PLC 第 {attempt} 次自动重连成功");
                PlcConnectionStatus = "PLC 连接：重连成功，正在确认心跳";
                PlcReconnectStatus =
                    $"自动重连：第 {attempt} 次尝试成功";
                PlcHeartbeatStatus = "心跳：正在确认链路";
                plcHeartbeatMonitor.Start();
                AddLog(
                    LogLevel.Success,
                    $"PLC 在第 {attempt} 次自动重连后恢复连接。");
                RefreshPlcCommands();
            });
        }

        private void OnSimulatedPlcClientFaulted(Exception exception)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
                AddLog(
                    LogLevel.Warning,
                    $"模拟 PLC 客户端通信结束：{exception.Message}"));
        }

        private void OnSimulatedPlcHandshakeStateChanged(string state)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PlcHandshakeStatus = $"检测握手：{state}";
                RefreshModbusRegisterStatus();
            });
        }

        private void OnModbusRequestProcessed(byte functionCode)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                modbusRequestCount++;
                ModbusTcpStatus =
                    $"Modbus TCP：监听 127.0.0.1:{modbusTcpServer.ListeningPort}，" +
                    $"请求数 {modbusRequestCount}，最近功能码 0x{functionCode:X2}";
                RefreshModbusRegisterStatus();
            });
        }

        private void OnModbusPollingStatusChanged(string status)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ModbusPollingStatus = $"Modbus 轮询：{status}";
                PlcHandshakeStatus = $"检测握手：{status}";
                RefreshModbusRegisterStatus();
                RefreshModbusCommands();
            });
        }

        private void OnModbusPollingFailed(Exception exception)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RaiseAlarm(
                    "MODBUS_CONNECTION_LOST",
                    AlarmSeverity.Error,
                    "PLC.ModbusTcp",
                    $"Modbus 周期轮询中断：{exception.Message}");
                modbusInspectionPollingService.Stop();
                ModbusPollingStatus = "Modbus 轮询：通信中断";
                ModbusClientStatus =
                    "Modbus 客户端：通信中断，进入自动重连";
                ModbusReconnectStatus = "Modbus 重连：1 秒后开始";
                AddLog(
                    LogLevel.Warning,
                    $"Modbus 周期轮询中断，进入自动重连：" +
                    exception.Message);
                modbusAutoReconnectService.Start(
                    lastConnectedModbusHost,
                    lastConnectedModbusPort);
                RefreshModbusCommands();
            });
        }

        private void OnModbusReconnectAttempting(int attempt)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ModbusReconnectStatus =
                    $"Modbus 重连：正在进行第 {attempt} 次尝试";
                RefreshModbusCommands();
            });
        }

        private void OnModbusReconnectAttemptFailed(
            int attempt,
            Exception exception,
            TimeSpan nextDelay)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ModbusClientStatus = "Modbus 客户端：仍处于断线状态";
                ModbusReconnectStatus =
                    $"Modbus 重连：第 {attempt} 次失败，" +
                    $"{nextDelay.TotalSeconds:F0} 秒后重试";

                if (attempt == 1 || attempt % 10 == 0)
                {
                    AddLog(
                        LogLevel.Warning,
                        $"Modbus 第 {attempt} 次自动重连失败：" +
                        exception.Message);
                }

                RefreshModbusCommands();
            });
        }

        private void OnModbusReconnected(int attempt)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ClearAlarm(
                    "MODBUS_CONNECTION_LOST",
                    "PLC.ModbusTcp",
                    $"Modbus 第 {attempt} 次自动重连成功");
                modbusInspectionPollingService.Start();
                ModbusClientStatus =
                    $"Modbus 客户端：已恢复连接 " +
                    $"{lastConnectedModbusHost}:{lastConnectedModbusPort}";
                ModbusReconnectStatus =
                    $"Modbus 重连：第 {attempt} 次尝试成功";
                AddLog(
                    LogLevel.Success,
                    $"Modbus 在第 {attempt} 次自动重连后恢复，" +
                    "周期轮询已自动重启。" );
                RefreshModbusCommands();
            });
        }

        private void RefreshModbusRegisterStatus()
        {
            ModbusDataStore dataStore = simulatedPlcServer.DataStore;
            bool[] coils = dataStore.ReadCoils(
                ModbusRegisterMap.StartRequestCoil,
                5);
            ushort cycleId = dataStore.ReadHoldingRegister(
                ModbusRegisterMap.CycleIdRegister);
            ushort resultCode = dataStore.ReadHoldingRegister(
                ModbusRegisterMap.ResultCodeRegister);

            ModbusRegisterStatus =
                $"Modbus 内存：START={(coils[0] ? 1 : 0)} " +
                $"BUSY={(coils[1] ? 1 : 0)} " +
                $"DONE={(coils[2] ? 1 : 0)} " +
                $"OK={(coils[3] ? 1 : 0)} " +
                $"NG={(coils[4] ? 1 : 0)}\n" +
                $"HR0 周期={cycleId}，HR1 结果码={resultCode}";
        }

        private bool CanStopCamera()
        {
            return HasPermission(UserPermission.OperateMachine) &&
                acquisitionService.IsRunning;
        }

        private bool CanStartDetect()
        {
            MachineState state = machineStateMachine.CurrentState;
            return HasPermission(UserPermission.OperateMachine) &&
                OperationModePolicy.CanExecuteDetection(
                    selectedOperationMode,
                    DetectionTriggerSource.ManualButton) &&
                acquisitionService.IsConnected &&
                (state == MachineState.Ready ||
                 state == MachineState.Completed);
        }

        private bool CanResetMachineState()
        {
            MachineState state = machineStateMachine.CurrentState;
            if (state == MachineState.Emergency)
            {
                return HasPermission(UserPermission.ResetEmergency);
            }

            return HasPermission(UserPermission.ResetFault) &&
                (state == MachineState.Completed ||
                 state == MachineState.Fault);
        }

        private bool CanAcceptAutomaticDetection()
        {
            MachineState state = machineStateMachine.CurrentState;
            return selectedOperationMode == OperationMode.Automatic &&
                (state == MachineState.Ready ||
                 state == MachineState.Completed);
        }

        private void OpenCamera()
        {
            if (!acquisitionService.OpenAndStart())
            {
                RaiseAlarm(
                    "CAMERA_CONNECTION_FAILURE",
                    AlarmSeverity.Error,
                    "Camera.Primary",
                    "相机打开失败");
                CameraStatus = "状态：连接失败";
                AddLog(LogLevel.Error, "相机连接失败。");
                TransitionMachineState(
                    MachineState.Fault,
                    "相机打开失败");
                WriteAudit(
                    "OpenCamera",
                    "Camera",
                    "Primary",
                    AuditOutcome.Failure,
                    "相机打开失败");
                return;
            }

            ClearAlarm(
                "CAMERA_CONNECTION_FAILURE",
                "Camera.Primary",
                "相机已成功打开");
            ClearAlarm(
                "CAMERA_ACQUISITION_FAILURE",
                "Camera.Primary",
                "相机采集已恢复");

            CameraStatus = "状态：相机已连接";
            TransitionMachineState(
                MachineState.Ready,
                "相机已连接并开始采集");
            AddLog(LogLevel.Success, "相机已打开，开始连续采集。");
            WriteAudit(
                "OpenCamera",
                "Camera",
                "Primary",
                AuditOutcome.Success,
                "相机已打开并开始采集");
            RefreshCameraCommands();
        }

        private void StopCamera()
        {
            acquisitionService.Stop();
            CameraStatus = "状态：相机已停止";
            TransitionMachineState(
                MachineState.Idle,
                "操作员停止相机");
            AddLog(LogLevel.Info, "相机采集已停止。");
            WriteAudit(
                "StopCamera",
                "Camera",
                "Primary",
                AuditOutcome.Success,
                "操作员停止相机采集");
            RefreshCameraCommands();
        }

        private void ResetMachineState()
        {
            bool isEmergency = machineStateMachine.CurrentState ==
                MachineState.Emergency;
            UserPermission requiredPermission = isEmergency
                ? UserPermission.ResetEmergency
                : UserPermission.ResetFault;
            if (!EnsurePermission(requiredPermission, "设备状态复位"))
            {
                return;
            }

            if (isEmergency)
            {
                acquisitionService.Stop();
            }

            MachineState targetState = !isEmergency &&
                acquisitionService.IsConnected
                    ? MachineState.Ready
                    : MachineState.Idle;
            bool transitionSucceeded = TransitionMachineState(
                targetState,
                isEmergency
                    ? "操作员确认安全条件后复位急停"
                    : targetState == MachineState.Ready
                    ? "操作员复位，设备条件正常"
                    : "操作员复位，设备保持空闲");
            if (transitionSucceeded && isEmergency)
            {
                ClearAlarm(
                    "EMERGENCY_STOP",
                    "Machine.Safety",
                    "操作员确认安全条件后复位急停");
            }
            WriteAudit(
                "ResetMachineState",
                "Machine",
                targetState.ToString(),
                transitionSucceeded
                    ? AuditOutcome.Success
                    : AuditOutcome.Failure,
                isEmergency ? "急停复位" : "设备状态复位");
        }

        private void EmergencyStop()
        {
            if (!EnsurePermission(UserPermission.OperateMachine, "急停"))
            {
                return;
            }

            if (!TransitionMachineState(
                    MachineState.Emergency,
                    "操作员按下急停"))
            {
                return;
            }

            acquisitionService.Stop();
            RaiseAlarm(
                "EMERGENCY_STOP",
                AlarmSeverity.Critical,
                "Machine.Safety",
                "操作员触发急停，设备停止运行");
            CameraStatus = "状态：急停已触发，相机采集已停止";
            AddLog(
                LogLevel.Error,
                "急停已触发：停止相机采集并禁止所有检测请求。" );
            WriteAudit(
                "EmergencyStop",
                "Machine",
                "Emergency",
                AuditOutcome.Success,
                "用户触发急停并停止相机采集");
            RefreshCameraCommands();
            RefreshPlcCommands();
            RefreshModbusCommands();
        }

        private InspectionResult? StartDetect(
            DetectionTriggerSource triggerSource,
            string? cycleId = null)
        {
            if (!OperationModePolicy.CanExecuteDetection(
                    selectedOperationMode,
                    triggerSource))
            {
                AddLog(
                    LogLevel.Warning,
                    $"检测请求被运行模式拒绝：当前为 " +
                    $"{(selectedOperationMode == OperationMode.Manual ? "手动" : "自动")}" +
                    $"模式，触发来源为 {GetTriggerSourceDisplayName(triggerSource)}。" );
                return null;
            }

            MachineState state = machineStateMachine.CurrentState;
            if (state != MachineState.Ready &&
                state != MachineState.Completed)
            {
                AddLog(
                    LogLevel.Warning,
                    $"检测请求被状态机拒绝：当前状态为 {state}。" );
                return null;
            }

            if (!acquisitionService.IsConnected)
            {
                CameraStatus = "状态：请先打开相机";
                AddLog(LogLevel.Warning, "检测未执行：相机尚未打开。");
                return null;
            }

            if (!VisionParameters.TryCreateParameters(
                    out VisionParameters? parameters,
                    out string? parameterError) ||
                parameters is null)
            {
                CameraStatus = "状态：视觉参数错误";
                AddLog(
                    LogLevel.Warning,
                    $"检测未执行：{parameterError}");
                return null;
            }

            using Mat? image = acquisitionService.GetLatestFrame();
            if (image is null)
            {
                CameraStatus = "状态：正在等待首帧图像";
                AddLog(LogLevel.Warning, "检测未执行：尚未收到图像。");
                return null;
            }

            if (state == MachineState.Completed &&
                !TransitionMachineState(
                    MachineState.Ready,
                    "开始下一个检测周期"))
            {
                return null;
            }

            if (!TransitionMachineState(
                    MachineState.Running,
                    "视觉检测开始"))
            {
                return null;
            }

            try
            {
                using VisionProcessingResult processingResult =
                    VisionProcessor.Process(
                        image,
                        parameters,
                        SelectedVisionDebugView);
                InspectionResult result = processingResult.Inspection;

                CameraImage = OpenCvHelper.MatToBitmapImage(
                    processingResult.DisplayImage);
                resultImageVisibleUntilUtc = DateTime.UtcNow.AddSeconds(2);

                Count = result.Count;
                RawContourCount = result.RawContourCount;
                Area = result.Area;
                PhysicalArea = result.PhysicalArea;
                ProcessingTimeMs = result.ProcessingTimeMs;
                centerX = result.CenterX;
                centerY = result.CenterY;
                centerXMillimeters = result.CenterXMillimeters;
                centerYMillimeters = result.CenterYMillimeters;
                widthPixels = result.WidthPixels;
                heightPixels = result.HeightPixels;
                widthMillimeters = result.WidthMillimeters;
                heightMillimeters = result.HeightMillimeters;
                OnPropertyChanged(nameof(Position));
                OnPropertyChanged(nameof(PhysicalPosition));
                OnPropertyChanged(nameof(PixelSize));
                OnPropertyChanged(nameof(PhysicalSize));
                JudgementReason = result.JudgementReason;
                CameraStatus = result.IsOK
                    ? "状态：检测合格"
                    : "状态：检测不合格";

                string roiDescription = parameters.IsRoiEnabled
                    ? $"({parameters.RoiX},{parameters.RoiY}," +
                      $"{parameters.RoiWidth},{parameters.RoiHeight})"
                    : "全图";

                AddLog(
                    result.IsOK ? LogLevel.Success : LogLevel.Warning,
                    $"检测完成：原始轮廓={result.RawContourCount}，" +
                    $"有效目标={result.Count}，面积={result.Area:F0}，" +
                    $"物理面积={result.PhysicalArea:F2}mm²，" +
                    $"中心=({result.CenterX},{result.CenterY})，" +
                    $"物理中心=({result.CenterXMillimeters:F2}," +
                    $"{result.CenterYMillimeters:F2})mm，" +
                    $"外接尺寸={result.WidthPixels}×" +
                    $"{result.HeightPixels}px，" +
                    $"物理尺寸={result.WidthMillimeters:F2}×" +
                    $"{result.HeightMillimeters:F2}mm，" +
                    $"宽度规格={parameters.MinimumWidthMillimeters:F2}～" +
                    $"{parameters.MaximumWidthMillimeters:F2}mm，" +
                    $"高度规格={parameters.MinimumHeightMillimeters:F2}～" +
                    $"{parameters.MaximumHeightMillimeters:F2}mm，" +
                    $"耗时={result.ProcessingTimeMs:F2}ms，ROI={roiDescription}，" +
                    $"阈值={parameters.BinaryThreshold}，" +
                    $"像素当量={parameters.MillimetersPerPixel:F4}mm/px，" +
                    $"期望数量={parameters.ExpectedTargetCount}，" +
                    $"合格面积={parameters.MinimumOkArea:F0}～" +
                    $"{parameters.MaximumOkArea:F0}，" +
                    $"高斯核={parameters.GaussianKernelSize}，" +
                    $"形态学核={parameters.MorphologyKernelSize}，" +
                    $"轮廓最小面积={parameters.MinimumContourArea:F0}，" +
                    $"最小圆度={parameters.MinimumCircularity:F2}，" +
                    $"结果={(result.IsOK ? "OK" : "NG")}，" +
                    $"原因={result.JudgementReason}。");

                string? ngImagePath = null;
                if (!result.IsOK)
                {
                    ngImagePath = SaveNgImage(
                        processingResult.AnnotatedImage,
                        result);
                }

                SaveInspectionHistory(
                    result,
                    triggerSource,
                    cycleId,
                    ngImagePath);

                TransitionMachineState(
                    MachineState.Completed,
                    result.IsOK
                        ? "检测完成，产品判定 OK"
                        : "检测完成，产品判定 NG");

                return result;
            }
            catch (Exception ex)
            {
                CameraStatus = "状态：检测失败";
                TransitionMachineState(
                    MachineState.Fault,
                    $"视觉检测异常：{ex.Message}");
                AddLog(LogLevel.Error, $"视觉检测失败：{ex.Message}");
                return null;
            }
        }

        private bool TransitionMachineState(
            MachineState targetState,
            string reason)
        {
            if (machineStateMachine.TryTransition(
                    targetState,
                    reason,
                    out string? errorMessage))
            {
                return true;
            }

            AddLog(
                LogLevel.Warning,
                $"设备状态转换被拒绝：{errorMessage}");
            return false;
        }

        private void OnMachineStateChanged(
            object? sender,
            MachineStateChangedEventArgs e)
        {
            void UpdateState()
            {
                if (e.CurrentState == MachineState.Fault)
                {
                    RaiseAlarm(
                        "MACHINE_FAULT",
                        AlarmSeverity.Error,
                        "Machine.State",
                        e.Reason);
                }
                else if (e.PreviousState == MachineState.Fault)
                {
                    ClearAlarm(
                        "MACHINE_FAULT",
                        "Machine.State",
                        $"设备状态已恢复为 {e.CurrentState}：{e.Reason}");
                }

                MachineStateStatus =
                    $"设备状态：{e.CurrentState}（" +
                    $"{GetMachineStateDisplayName(e.CurrentState)}）";
                MachineStateReason =
                    $"状态原因：{e.Reason}，{e.ChangedAt:HH:mm:ss}";
                OnPropertyChanged(nameof(CanChangeOperationMode));
                applyBatchNumberCommand.RaiseCanExecuteChanged();
                AddLog(
                    e.CurrentState == MachineState.Fault
                        ? LogLevel.Error
                        : LogLevel.Info,
                    $"设备状态：{e.PreviousState} → " +
                    $"{e.CurrentState}，原因：{e.Reason}。" );
                RefreshCameraCommands();
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                UpdateState();
            }
            else
            {
                _ = Application.Current.Dispatcher.InvokeAsync(UpdateState);
            }
        }

        private static string GetMachineStateDisplayName(MachineState state)
        {
            return state switch
            {
                MachineState.Idle => "空闲",
                MachineState.Ready => "就绪",
                MachineState.Running => "运行中",
                MachineState.Completed => "已完成",
                MachineState.Fault => "故障",
                MachineState.Emergency => "急停",
                _ => "未知"
            };
        }

        private static string GetTriggerSourceDisplayName(
            DetectionTriggerSource triggerSource)
        {
            return triggerSource switch
            {
                DetectionTriggerSource.ManualButton => "界面按钮",
                DetectionTriggerSource.TextPlc => "文本 PLC",
                DetectionTriggerSource.Modbus => "Modbus",
                _ => "未知来源"
            };
        }

        private void SaveRecipe()
        {
            if (!EnsurePermission(UserPermission.SaveRecipe, "保存配方"))
            {
                return;
            }

            if (!VisionParameters.TryCreateParameters(
                    out VisionParameters? parameters,
                    out string? parameterError) ||
                parameters is null)
            {
                RecipeStatus = "配方：保存失败";
                AddLog(
                    LogLevel.Warning,
                    $"配方保存失败：{parameterError}");
                WriteAudit(
                    "SaveRecipe",
                    "VisionRecipe",
                    RecipeNameInput,
                    AuditOutcome.Failure,
                    $"视觉参数校验失败：{parameterError}");
                return;
            }

            if (!visionRecipeService.TrySave(
                    RecipeNameInput,
                    parameters,
                    out int savedRecipeRevision,
                    out string? errorMessage))
            {
                RecipeStatus = "配方：保存失败";
                AddLog(
                    LogLevel.Error,
                    $"配方文件写入失败：{errorMessage}");
                WriteAudit(
                    "SaveRecipe",
                    "VisionRecipe",
                    RecipeNameInput,
                    AuditOutcome.Failure,
                    errorMessage ?? "配方文件写入失败");
                return;
            }

            activeRecipeName = RecipeNameInput.Trim();
            activeRecipeRevision = savedRecipeRevision;
            recipeHasUnsavedChanges = false;
            RecipeNameInput = activeRecipeName;
            RecipeStatus =
                $"配方：{activeRecipeName} V{activeRecipeRevision}（已保存）";
            AddLog(
                LogLevel.Success,
                $"视觉配方已保存：{activeRecipeName} " +
                $"V{activeRecipeRevision}，{visionRecipeService.RecipePath}");
            WriteAudit(
                "SaveRecipe",
                "VisionRecipe",
                activeRecipeName,
                AuditOutcome.Success,
                $"保存配方版本 V{activeRecipeRevision}");
        }

        private string? SaveNgImage(
            Mat annotatedImage,
            InspectionResult result)
        {
            if (ngImageStorageService.TrySave(
                    annotatedImage,
                    result,
                    out string? savedPath,
                    out string? errorMessage))
            {
                AddLog(LogLevel.Info, $"NG 标注图已保存：{savedPath}");
                CleanupNgImages();
                return savedPath;
            }

            AddLog(LogLevel.Error, $"NG 标注图保存失败：{errorMessage}");
            return null;
        }

        private void InitializeInspectionDatabase()
        {
            if (!inspectionHistoryService.TryInitialize(
                    out string? initializeError))
            {
                DatabaseStatus = "检测数据库：初始化失败";
                AddLog(
                    LogLevel.Error,
                    $"检测数据库初始化失败：{initializeError}");
                return;
            }

            if (!inspectionHistoryService.TryGetRecordCount(
                    out long recordCount,
                    out string? countError))
            {
                DatabaseStatus = "检测数据库：已创建，统计失败";
                AddLog(
                    LogLevel.Warning,
                    $"检测数据库记录数读取失败：{countError}");
                return;
            }

            DatabaseStatus = $"检测数据库：已连接，共 {recordCount} 条记录";
            AddLog(
                LogLevel.Success,
                $"检测数据库已就绪：{inspectionHistoryService.DatabasePath}");
            QueryInspectionHistory(false, true);
        }

        private bool CanApplyBatchNumber()
        {
            MachineState state = machineStateMachine.CurrentState;
            return HasPermission(UserPermission.ChangeBatch) &&
                state != MachineState.Running &&
                state != MachineState.Emergency;
        }

        private void ApplyBatchNumber()
        {
            if (!EnsurePermission(UserPermission.ChangeBatch, "切换生产批次"))
            {
                return;
            }

            if (!CanApplyBatchNumber())
            {
                AddLog(
                    LogLevel.Warning,
                    $"当前设备状态 {machineStateMachine.CurrentState} " +
                    "不允许切换生产批次。");
                return;
            }

            string candidate = (BatchNumberInput ?? string.Empty).Trim();
            if (candidate.Length == 0)
            {
                AddLog(LogLevel.Warning, "批次切换失败：批次号不能为空。");
                return;
            }

            if (candidate.Length > 50)
            {
                AddLog(
                    LogLevel.Warning,
                    "批次切换失败：批次号最多允许 50 个字符。");
                return;
            }

            foreach (char character in candidate)
            {
                if (char.IsControl(character))
                {
                    AddLog(
                        LogLevel.Warning,
                        "批次切换失败：批次号不能包含换行等控制字符。");
                    return;
                }
            }

            if (string.Equals(
                    activeBatchNumber,
                    candidate,
                    StringComparison.Ordinal))
            {
                BatchNumberInput = candidate;
                AddLog(LogLevel.Info, $"当前已经是批次 {candidate}。");
                return;
            }

            string previousBatchNumber = activeBatchNumber;
            activeBatchNumber = candidate;
            BatchNumberInput = candidate;
            OnPropertyChanged(nameof(ActiveBatchStatus));
            AddLog(
                LogLevel.Success,
                $"生产批次已切换：{previousBatchNumber} → {candidate}。" );
            WriteAudit(
                "ChangeBatch",
                "ProductionBatch",
                candidate,
                AuditOutcome.Success,
                $"批次由 {previousBatchNumber} 切换为 {candidate}");
        }

        private void SaveInspectionHistory(
            InspectionResult result,
            DetectionTriggerSource triggerSource,
            string? cycleId,
            string? ngImagePath)
        {
            var record = new InspectionHistoryRecord
            {
                BatchNumber = activeBatchNumber,
                RecipeName = recipeHasUnsavedChanges
                    ? $"{activeRecipeName}（未保存修改）"
                    : activeRecipeName,
                RecipeRevision = recipeHasUnsavedChanges
                    ? 0
                    : activeRecipeRevision,
                DetectedAtUtc = DateTime.UtcNow,
                TriggerSource = triggerSource.ToString(),
                CycleId = cycleId,
                OperationMode = selectedOperationMode.ToString(),
                IsOk = result.IsOK,
                JudgementCode = result.JudgementCode.ToString(),
                JudgementReason = result.JudgementReason,
                TargetCount = result.Count,
                RawContourCount = result.RawContourCount,
                AreaPixels = result.Area,
                PhysicalAreaSquareMillimeters = result.PhysicalArea,
                CenterXPixel = result.CenterX,
                CenterYPixel = result.CenterY,
                CenterXMillimeters = result.CenterXMillimeters,
                CenterYMillimeters = result.CenterYMillimeters,
                WidthPixels = result.WidthPixels,
                HeightPixels = result.HeightPixels,
                WidthMillimeters = result.WidthMillimeters,
                HeightMillimeters = result.HeightMillimeters,
                ProcessingTimeMilliseconds = result.ProcessingTimeMs,
                NgImagePath = ngImagePath
            };

            if (!inspectionHistoryService.TrySave(
                    record,
                    out string? saveError))
            {
                DatabaseStatus = "检测数据库：最近一次写入失败";
                AddLog(
                    LogLevel.Error,
                    $"检测历史写入失败：{saveError}");
                return;
            }

            if (inspectionHistoryService.TryGetRecordCount(
                    out long recordCount,
                    out _))
            {
                DatabaseStatus =
                    $"检测数据库：已连接，共 {recordCount} 条记录";
            }
            else
            {
                DatabaseStatus = "检测数据库：最近一次写入成功";
            }

            QueryInspectionHistory(false, true);
        }

        private void QueryInspectionHistory(
            bool writeLog,
            bool resetPage)
        {
            if (!HistoryStartDate.HasValue || !HistoryEndDate.HasValue)
            {
                HistoryQueryStatus = "历史查询：请选择开始和结束日期";
                ResetHistoryPagination();
                return;
            }

            DateTime startLocal = DateTime.SpecifyKind(
                HistoryStartDate.Value.Date,
                DateTimeKind.Local);
            DateTime endExclusiveLocal = DateTime.SpecifyKind(
                HistoryEndDate.Value.Date.AddDays(1),
                DateTimeKind.Local);

            if (endExclusiveLocal <= startLocal)
            {
                HistoryQueryStatus = "历史查询：结束日期不能早于开始日期";
                ResetHistoryPagination();
                return;
            }

            if (resetPage)
            {
                historyPageNumber = 1;
            }

            if (!inspectionHistoryService.TryQueryPage(
                    startLocal.ToUniversalTime(),
                    endExclusiveLocal.ToUniversalTime(),
                    SelectedHistoryResultFilter,
                    HistoryBatchNumber,
                    historyPageNumber,
                    HistoryPageSize,
                    out IReadOnlyList<InspectionHistoryRecord> records,
                    out long totalMatchingRecords,
                    out string? errorMessage))
            {
                HistoryQueryStatus = $"历史查询失败：{errorMessage}";
                ResetHistoryPagination();
                if (writeLog)
                {
                    AddLog(
                        LogLevel.Error,
                        $"检测历史查询失败：{errorMessage}");
                }
                return;
            }

            historyTotalPages = Math.Max(
                1,
                (int)Math.Ceiling(
                    totalMatchingRecords / (double)HistoryPageSize));

            HistoryRecords.Clear();
            foreach (InspectionHistoryRecord record in records)
            {
                HistoryRecords.Add(record);
            }

            HistoryQueryStatus =
                $"第 {historyPageNumber}/{historyTotalPages} 页，" +
                $"本页 {records.Count} 条，筛选共 {totalMatchingRecords} 条";

            if (inspectionHistoryService.TryGetSummary(
                    startLocal.ToUniversalTime(),
                    endExclusiveLocal.ToUniversalTime(),
                    HistoryBatchNumber,
                    out InspectionHistorySummary summary,
                    out string? summaryError))
            {
                HistoryTotalText = $"总数：{summary.TotalCount}";
                HistoryOkText = $"OK：{summary.OkCount}";
                HistoryNgText = $"NG：{summary.NgCount}";
                HistoryPassRateText =
                    $"合格率：{summary.PassRate:F2}%";
            }
            else
            {
                HistoryTotalText = "总数：统计失败";
                HistoryOkText = "OK：--";
                HistoryNgText = "NG：--";
                HistoryPassRateText = "合格率：--";
                if (writeLog)
                {
                    AddLog(
                        LogLevel.Warning,
                        $"检测历史统计失败：{summaryError}");
                }
            }

            RefreshHistoryPaginationCommands();
            if (writeLog)
            {
                AddLog(
                    LogLevel.Info,
                    $"检测历史查询完成，共显示 {records.Count} 条。" );
            }
        }

        private void MoveToHistoryPage(int pageNumber)
        {
            if (pageNumber < 1 || pageNumber > historyTotalPages)
            {
                return;
            }

            historyPageNumber = pageNumber;
            QueryInspectionHistory(false, false);
        }

        private void ResetHistoryPagination()
        {
            historyPageNumber = 1;
            historyTotalPages = 1;
            HistoryRecords.Clear();
            HistoryTotalText = "总数：0";
            HistoryOkText = "OK：0";
            HistoryNgText = "NG：0";
            HistoryPassRateText = "合格率：0.00%";
            RefreshHistoryPaginationCommands();
        }

        private void InvalidateHistoryQuery()
        {
            HistoryQueryStatus = "查询条件已改变，请点击查询/刷新";
            ResetHistoryPagination();
        }

        private void RefreshHistoryPaginationCommands()
        {
            firstHistoryPageCommand.RaiseCanExecuteChanged();
            previousHistoryPageCommand.RaiseCanExecuteChanged();
            nextHistoryPageCommand.RaiseCanExecuteChanged();
            lastHistoryPageCommand.RaiseCanExecuteChanged();
        }

        private async void ExportHistoryCsv()
        {
            if (!EnsurePermission(
                    UserPermission.ExportHistory,
                    "导出检测历史"))
            {
                return;
            }

            if (!HistoryStartDate.HasValue || !HistoryEndDate.HasValue)
            {
                HistoryQueryStatus = "CSV导出：请选择开始和结束日期";
                return;
            }

            DateTime startLocal = DateTime.SpecifyKind(
                HistoryStartDate.Value.Date,
                DateTimeKind.Local);
            DateTime endExclusiveLocal = DateTime.SpecifyKind(
                HistoryEndDate.Value.Date.AddDays(1),
                DateTimeKind.Local);
            if (endExclusiveLocal <= startLocal)
            {
                HistoryQueryStatus = "CSV导出：结束日期不能早于开始日期";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "导出检测历史 CSV",
                Filter = "CSV 文件 (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = $"检测历史_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            InspectionResultFilter exportResultFilter =
                SelectedHistoryResultFilter;
            string exportBatchNumber =
                (HistoryBatchNumber ?? string.Empty).Trim();

            isHistoryExporting = true;
            exportHistoryCsvCommand.RaiseCanExecuteChanged();
            HistoryQueryStatus = "CSV导出：正在生成文件...";

            try
            {
                (bool Success, long Count, string? Error) exportResult =
                    await Task.Run(() =>
                    {
                        bool success =
                            inspectionHistoryCsvExportService.TryExport(
                                dialog.FileName,
                                startLocal.ToUniversalTime(),
                                endExclusiveLocal.ToUniversalTime(),
                                exportResultFilter,
                                exportBatchNumber,
                                out long exportedCount,
                                out string? exportError);
                        return (success, exportedCount, exportError);
                    });

                if (!exportResult.Success)
                {
                    HistoryQueryStatus =
                        $"CSV导出失败：{exportResult.Error}";
                    AddLog(
                        LogLevel.Error,
                        $"检测历史 CSV 导出失败：{exportResult.Error}");
                    WriteAudit(
                        "ExportHistoryCsv",
                        "Report",
                        dialog.FileName,
                        AuditOutcome.Failure,
                        exportResult.Error ?? "CSV导出失败");
                    return;
                }

                HistoryQueryStatus =
                    $"CSV导出成功：共 {exportResult.Count} 条";
                AddLog(
                    LogLevel.Success,
                    $"检测历史 CSV 已导出：{dialog.FileName}，" +
                    $"共 {exportResult.Count} 条。" );
                WriteAudit(
                    "ExportHistoryCsv",
                    "Report",
                    dialog.FileName,
                    AuditOutcome.Success,
                    $"导出 {exportResult.Count} 条检测记录");
            }
            catch (Exception ex)
            {
                HistoryQueryStatus = $"CSV导出失败：{ex.Message}";
                AddLog(
                    LogLevel.Error,
                    $"检测历史 CSV 导出异常：{ex.Message}");
                WriteAudit(
                    "ExportHistoryCsv",
                    "Report",
                    dialog.FileName,
                    AuditOutcome.Failure,
                    ex.Message);
            }
            finally
            {
                isHistoryExporting = false;
                exportHistoryCsvCommand.RaiseCanExecuteChanged();
            }
        }

        private void CleanupNgImages()
        {
            if (!ngImageStorageService.TryCleanup(
                    out int deletedFileCount,
                    out string? errorMessage))
            {
                AddLog(LogLevel.Warning, $"NG 图片自动清理失败：{errorMessage}");
                return;
            }

            if (deletedFileCount > 0)
            {
                AddLog(
                    LogLevel.Info,
                    $"NG 图片自动清理完成，共删除 {deletedFileCount} 张旧图片。");
            }
        }

        private void LoadRecipe(bool isApplicationStartup)
        {
            if (visionRecipeService.TryLoad(
                    out VisionParameters? parameters,
                    out string loadedRecipeName,
                    out int loadedRecipeRevision,
                    out string? errorMessage) &&
                parameters is not null)
            {
                isApplyingRecipe = true;
                try
                {
                    VisionParameters.ApplyParameters(parameters);
                }
                finally
                {
                    isApplyingRecipe = false;
                }

                activeRecipeName = loadedRecipeName;
                activeRecipeRevision = loadedRecipeRevision;
                recipeHasUnsavedChanges = false;
                RecipeNameInput = loadedRecipeName;
                RecipeStatus = isApplicationStartup
                    ? $"配方：{loadedRecipeName} V{loadedRecipeRevision}（启动恢复）"
                    : $"配方：{loadedRecipeName} V{loadedRecipeRevision}（已加载）";
                AddLog(
                    LogLevel.Success,
                    $"视觉配方已加载：{loadedRecipeName} " +
                    $"V{loadedRecipeRevision}，{visionRecipeService.RecipePath}");
                WriteAudit(
                    "LoadRecipe",
                    "VisionRecipe",
                    loadedRecipeName,
                    AuditOutcome.Success,
                    $"加载 V{loadedRecipeRevision}" +
                    (isApplicationStartup ? "（启动恢复）" : string.Empty));
                return;
            }

            if (errorMessage is not null)
            {
                RecipeStatus = "配方：加载失败";
                AddLog(
                    LogLevel.Error,
                    $"配方加载失败：{errorMessage}");
                WriteAudit(
                    "LoadRecipe",
                    "VisionRecipe",
                    RecipeNameInput,
                    AuditOutcome.Failure,
                    errorMessage);
                return;
            }

            RecipeStatus = "配方：内置默认参数";
            AddLog(
                isApplicationStartup ? LogLevel.Info : LogLevel.Warning,
                isApplicationStartup
                    ? "未找到已保存配方，当前使用内置默认参数。"
                    : "没有可加载的配方文件，请先保存配方。");
            if (!isApplicationStartup)
            {
                WriteAudit(
                    "LoadRecipe",
                    "VisionRecipe",
                    RecipeNameInput,
                    AuditOutcome.Failure,
                    "未找到可加载的配方文件");
            }
        }

        private void OnVisionParametersChanged(
            object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (isApplyingRecipe || recipeHasUnsavedChanges)
            {
                return;
            }

            recipeHasUnsavedChanges = true;
            RecipeStatus =
                $"配方：{activeRecipeName} V{activeRecipeRevision}" +
                "（参数已修改，未保存）";
            WriteAudit(
                "ModifyVisionParameters",
                "VisionRecipe",
                activeRecipeName,
                AuditOutcome.Success,
                $"参数已修改，首个变更属性：{e.PropertyName ?? "Unknown"}");
        }

        private void OnFrameReceived(BitmapImage bitmap)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (DateTime.UtcNow >= resultImageVisibleUntilUtc)
                {
                    CameraImage = bitmap;
                }

                FrameCount++;
            });
        }

        private void OnAcquisitionFailed(Exception exception)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RaiseAlarm(
                    "CAMERA_ACQUISITION_FAILURE",
                    AlarmSeverity.Error,
                    "Camera.Primary",
                    $"相机采集异常：{exception.Message}");
                CameraStatus = $"状态：采集失败（{exception.Message}）";
                TransitionMachineState(
                    MachineState.Fault,
                    $"相机采集异常：{exception.Message}");
                AddLog(LogLevel.Error, $"相机采集异常：{exception.Message}");
            });
        }

        private void OnAcquisitionStopped()
        {
            if (!Application.Current.Dispatcher.HasShutdownStarted)
            {
                _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    RefreshCameraCommands());
            }
        }

        private void OnReconnecting(int attempt, int maximumAttempts)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CameraStatus =
                    $"状态：相机断线，正在重连（{attempt}/{maximumAttempts}）";
                TransitionMachineState(
                    MachineState.Fault,
                    $"相机断线，正在进行第 {attempt} 次重连");
                AddLog(
                    LogLevel.Warning,
                    $"相机断线，正在进行第 {attempt}/{maximumAttempts} 次重连。");
            });
        }

        private void OnReconnected(int attempt)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ClearAlarm(
                    "CAMERA_CONNECTION_FAILURE",
                    "Camera.Primary",
                    $"相机第 {attempt} 次重连成功");
                ClearAlarm(
                    "CAMERA_ACQUISITION_FAILURE",
                    "Camera.Primary",
                    $"相机第 {attempt} 次重连后恢复采集");
                CameraStatus = "状态：相机重连成功";
                TransitionMachineState(
                    MachineState.Ready,
                    $"相机在第 {attempt} 次尝试后重连成功");
                AddLog(
                    LogLevel.Success,
                    $"相机在第 {attempt} 次尝试后重连成功，恢复采集。");
            });
        }

        private void RefreshCameraCommands()
        {
            openCameraCommand.RaiseCanExecuteChanged();
            stopCameraCommand.RaiseCanExecuteChanged();
            startDetectCommand.RaiseCanExecuteChanged();
            resetMachineStateCommand.RaiseCanExecuteChanged();
            emergencyStopCommand.RaiseCanExecuteChanged();
        }

        private void AddLog(LogLevel level, string message)
        {
            var entry = new LogEntry(DateTime.Now, level, message);
            Logs.Add(entry);

            const int maximumLogCount = 200;
            if (Logs.Count > maximumLogCount)
            {
                Logs.RemoveAt(0);
            }

            if (!fileLogService.TryWrite(entry, out string? errorMessage) &&
                !logWriteFailureReported)
            {
                logWriteFailureReported = true;
                Logs.Add(new LogEntry(
                    DateTime.Now,
                    LogLevel.Error,
                    $"日志文件写入失败：{errorMessage}"));
            }
        }

        private static string GetScenarioDisplayName(FakeCameraScenario scenario)
        {
            return scenario switch
            {
                FakeCameraScenario.StandardSingle => "标准单目标",
                FakeCameraScenario.DoubleTarget => "双目标",
                FakeCameraScenario.SmallTargetNg => "小目标 NG",
                FakeCameraScenario.MovingTarget => "移动目标",
                FakeCameraScenario.DynamicDemo => "动态综合",
                FakeCameraScenario.NoisyTarget => "噪声目标",
                FakeCameraScenario.CaptureFailure => "采集异常",
                _ => "未知场景"
            };
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            acquisitionService.FrameReceived -= OnFrameReceived;
            VisionParameters.PropertyChanged -= OnVisionParametersChanged;
            acquisitionService.AcquisitionFailed -= OnAcquisitionFailed;
            acquisitionService.AcquisitionStopped -= OnAcquisitionStopped;
            acquisitionService.Reconnecting -= OnReconnecting;
            acquisitionService.Reconnected -= OnReconnected;
            machineStateMachine.StateChanged -= OnMachineStateChanged;
            simulatedPlcServer.MessageProcessed -=
                OnSimulatedPlcMessageProcessed;
            simulatedPlcServer.ClientFaulted -= OnSimulatedPlcClientFaulted;
            simulatedPlcServer.HandshakeStateChanged -=
                OnSimulatedPlcHandshakeStateChanged;
            modbusTcpServer.RequestProcessed -= OnModbusRequestProcessed;
            modbusInspectionPollingService.StatusChanged -=
                OnModbusPollingStatusChanged;
            modbusInspectionPollingService.PollingFailed -=
                OnModbusPollingFailed;
            modbusAutoReconnectService.ReconnectAttempting -=
                OnModbusReconnectAttempting;
            modbusAutoReconnectService.ReconnectAttemptFailed -=
                OnModbusReconnectAttemptFailed;
            modbusAutoReconnectService.Reconnected -=
                OnModbusReconnected;
            plcClient.UnsolicitedMessageReceived -=
                OnPlcUnsolicitedMessageReceived;
            plcHeartbeatMonitor.HeartbeatSucceeded -= OnHeartbeatSucceeded;
            plcHeartbeatMonitor.HeartbeatFailed -= OnHeartbeatFailed;
            plcAutoReconnectService.ReconnectAttempting -=
                OnReconnectAttempting;
            plcAutoReconnectService.ReconnectAttemptFailed -=
                OnReconnectAttemptFailed;
            plcAutoReconnectService.Reconnected -= OnPlcReconnected;
            acquisitionService.Dispose();
            plcAutoReconnectService.Dispose();
            plcHeartbeatMonitor.Dispose();
            plcClient.Dispose();
            modbusInspectionPollingService.Dispose();
            modbusAutoReconnectService.Dispose();
            modbusTcpClient.Dispose();
            modbusTcpServer.Dispose();
            simulatedPlcServer.Dispose();
            disposed = true;
        }
    }
}
