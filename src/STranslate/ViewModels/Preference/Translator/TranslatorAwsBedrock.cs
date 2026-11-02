using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using STranslate.Helper;
using STranslate.Log;
using STranslate.Model;
using STranslate.Util;

namespace STranslate.ViewModels.Preference.Translator;

public partial class TranslatorAwsBedrock : TranslatorLLMBase, ITranslatorLLM
{
    #region Constructor

    public TranslatorAwsBedrock()
        : this(Guid.NewGuid(), "https://bedrock-runtime.us-east-1.amazonaws.com", "AWS Bedrock")
    {
    }

    public TranslatorAwsBedrock(
        Guid guid,
        string url,
        string name = "",
        IconType icon = IconType.AwsBedrock,
        string appID = "",
        string appKey = "",
        bool isEnabled = true,
        ServiceType type = ServiceType.AwsBedrockService
    )
    {
        // 需先订阅, 确保 Url 赋值时能够同步推导默认 Region
        PropertyChanged += OnUrlChangedSyncRegion;

        Identify = guid;
        Url = url;
        Name = name;
        Icon = icon;
        AppID = appID;
        AppKey = appKey;
        IsEnabled = isEnabled;
        Type = type;
    }

    #endregion Constructor

    #region Properties

    /// <summary>
    ///     鉴权方式: API密钥(Bedrock API Key) 或 AccessKey/SecretKey(IAM)
    /// </summary>
    [JsonIgnore] [ObservableProperty] private AwsBedrockAuthTypeEnum _authType = AwsBedrockAuthTypeEnum.ApiKey;

    /// <summary>
    ///     SigV4 签名使用的 region
    ///     * 默认跟随接口地址(Url)中的 region 自动填充, 用户手动修改后不再跟随 Url 变化
    /// </summary>
    [JsonIgnore] [ObservableProperty] private string _region = "us-east-1";

    /// <summary>
    ///     记录上一次由 Url 自动推导出的 Region, 用于判断用户是否手动自定义过 Region
    /// </summary>
    [JsonIgnore] private string _lastSyncedRegion = "us-east-1";

    private void OnUrlChangedSyncRegion(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Url)) return;

        var derived = DeriveRegionFromUrl(Url);

        // Region 仍等于上一次自动推导的值, 说明用户未手动自定义, 跟随 Url 更新
        if (Region == _lastSyncedRegion)
            Region = derived;

        _lastSyncedRegion = derived;
    }

    private static string DeriveRegionFromUrl(string url)
    {
        var host = (url ?? string.Empty).TrimEnd('/')
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase);
        var parts = host.Split('.');
        return parts.Length > 1 ? parts[1] : "us-east-1";
    }

    [JsonIgnore] private string _model = "anthropic.claude-3-5-sonnet-20241022-v2:0";
    public override string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    [JsonIgnore]
    private BindingList<string> _models =
    [
        "anthropic.claude-3-5-sonnet-20241022-v2:0",
        "anthropic.claude-3-5-haiku-20241022-v1:0",
        "anthropic.claude-3-haiku-20240307-v1:0",
        "amazon.nova-pro-v1:0",
        "amazon.nova-lite-v1:0",
        "meta.llama3-1-70b-instruct-v1:0",
        "mistral.mistral-large-2407-v1:0",
    ];
    public override BindingList<string> Models
    {
        get => _models;
        set => SetProperty(ref _models, value);
    }

    #endregion Properties

    #region Translator Test

    [property: JsonIgnore]
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task TestAsync(CancellationToken token)
    {
        var result = "";
        var isCancel = false;
        try
        {
            IsTesting = true;
            var reqModel = new RequestModel("你好", LangEnum.zh_cn, LangEnum.en);
            await TranslateAsync(reqModel, _ => result = AppLanguageManager.GetString("Toast.VerifySuccess"), token);
        }
        catch (OperationCanceledException)
        {
            isCancel = true;
        }
        catch (Exception)
        {
            result = AppLanguageManager.GetString("Toast.VerifyFailed");
        }
        finally
        {
            IsTesting = false;
            if (!isCancel)
                ToastHelper.Show(result, WindowType.Preference);
        }
    }

    #endregion Translator Test

    #region Interface Implementation

    public async Task TranslateAsync(object request, Action<string> onDataReceived, CancellationToken token)
    {
        if (string.IsNullOrEmpty(Url))
            throw new Exception("请先完善配置");

        if (request is not RequestModel req)
            throw new Exception($"请求数据出错: {request}");

        //检查语种
        var source = LangConverter(req.SourceLang) ?? throw new Exception($"该服务不支持{req.SourceLang.GetDescription()}");
        var target = LangConverter(req.TargetLang) ?? throw new Exception($"该服务不支持{req.TargetLang.GetDescription()}");
        var content = req.Text;

        // 选择模型
        var a_model = Model.Trim();
        a_model = string.IsNullOrEmpty(a_model) ? "anthropic.claude-3-5-sonnet-20241022-v2:0" : a_model;

        // 温度限定
        var a_temperature = Math.Clamp(Temperature, 0, 1);

        // 替换Prompt关键字
        var a_messages =
            (UserDefinePrompts.FirstOrDefault(x => x.Enabled)?.Prompts ?? throw new Exception("请先完善Propmpt配置")).Clone();
        a_messages.ToList().ForEach(item =>
            item.Content = item.Content.Replace("$source", source).Replace("$target", target)
                .Replace("$content", content));

        var systemMsg =
            a_messages.FirstOrDefault(x => x.Role.Equals("system", StringComparison.CurrentCultureIgnoreCase));
        if (systemMsg != null)
            a_messages.Remove(systemMsg);

        // https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_Converse.html
        // Converse 接口对所有 Bedrock 模型提供统一的请求/响应结构，避免因模型厂商不同而适配不同的请求体
        var a_bedrockMessages = a_messages.Select(x => new
        {
            role = x.Role.Equals("assistant", StringComparison.CurrentCultureIgnoreCase) ? "assistant" : "user",
            content = new object[] { new { text = x.Content } }
        }).ToArray();

        object reqData = systemMsg != null
            ? new
            {
                messages = a_bedrockMessages,
                system = new object[] { new { text = systemMsg.Content } },
                inferenceConfig = new { temperature = a_temperature, maxTokens = 1024 }
            }
            : new
            {
                messages = a_bedrockMessages,
                inferenceConfig = new { temperature = a_temperature, maxTokens = 1024 }
            };

        var jsonData = JsonConvert.SerializeObject(reqData);

        // 解析 Endpoint host, 如 https://bedrock-runtime.us-east-1.amazonaws.com
        var endpoint = Url.TrimEnd('/');
        var host = endpoint
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase);

        // 签名使用的 region 单独配置, 不强制要求与接口地址中的 region 一致 (自定义域名/VPC Endpoint 场景)
        var region = string.IsNullOrWhiteSpace(Region) ? "us-east-1" : Region.Trim();

        var path = $"/model/{AwsUriEncode(a_model)}/converse";
        var requestUrl = $"{endpoint}{path}";

        try
        {
            Dictionary<string, string> headers;
            if (AuthType == AwsBedrockAuthTypeEnum.ApiKey)
            {
                if (string.IsNullOrEmpty(AppKey))
                    throw new Exception("请先完善配置: API Key");

                headers = new Dictionary<string, string> { { "Authorization", $"Bearer {AppKey}" } };
            }
            else
            {
                if (string.IsNullOrEmpty(AppID) || string.IsNullOrEmpty(AppKey))
                    throw new Exception("请先完善配置: AccessKeyId/SecretAccessKey");

                headers = BuildSigV4Headers(host, path, jsonData, region, AppID, AppKey);
            }

            var resp = await HttpUtil.PostAsync(requestUrl, jsonData, null, headers, token).ConfigureAwait(false);

            var parsedData = JsonConvert.DeserializeObject<JObject>(resp) ?? throw new Exception("请求结果为空");
            var text = parsedData["output"]?["message"]?["content"]?.FirstOrDefault()?["text"]?.ToString();

            if (string.IsNullOrEmpty(text))
                throw new Exception(parsedData["message"]?.ToString() ?? "获取结果为空");

            onDataReceived?.Invoke(text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == null)
        {
            var msg = $"请检查服务是否可以正常访问: {Name} ({Url}).";
            throw new HttpRequestException(msg);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (ex.InnerException is { } innEx)
            {
                msg += $" {innEx.Message}";
                LogService.Logger.Error($"({Name})({Identify}) raw content:\n{innEx.Message}");
            }

            msg = msg.Trim();

            throw new Exception(msg);
        }
    }

    public Task<TranslationResult> TranslateAsync(object request, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public ITranslator Clone()
    {
        return new TranslatorAwsBedrock
        {
            Identify = Identify,
            Type = Type,
            Temperature = Temperature,
            IsEnabled = IsEnabled,
            Icon = Icon,
            Name = Name,
            Url = Url,
            Data = TranslationResult.Reset,
            AppID = AppID,
            AppKey = AppKey,
            AuthType = AuthType,
            Region = Region,
            UserDefinePrompts = UserDefinePrompts.Clone(),
            AutoExecute = AutoExecute,
            IdHide = IdHide,
            KeyHide = KeyHide,
            Model = Model,
            Models = Models,
            IsExecuting = IsExecuting,
            IsTranslateBackExecuting = IsTranslateBackExecuting,
            AutoExecuteTranslateBack = AutoExecuteTranslateBack,
        };
    }

    /// <summary>
    ///     https://zh.wikipedia.org/wiki/ISO_639-1%E4%BB%A3%E7%A0%81%E5%88%97%E8%A1%A8
    /// </summary>
    /// <param name="lang"></param>
    /// <returns></returns>
    public string? LangConverter(LangEnum lang)
    {
        return lang switch
        {
            LangEnum.auto => "auto",
            LangEnum.zh_cn => "Simplified Chinese",
            LangEnum.zh_tw => "Traditional Chinese",
            LangEnum.yue => "Cantonese",
            LangEnum.ja => "Japanese",
            LangEnum.en => "English",
            LangEnum.ko => "Korean",
            LangEnum.fr => "French",
            LangEnum.es => "Spanish",
            LangEnum.ru => "Russian",
            LangEnum.de => "German",
            LangEnum.it => "Italian",
            LangEnum.tr => "Turkish",
            LangEnum.pt_pt => "Portuguese",
            LangEnum.pt_br => "Portuguese",
            LangEnum.vi => "Vietnamese",
            LangEnum.id => "Indonesian",
            LangEnum.th => "Thai",
            LangEnum.ms => "Malay",
            LangEnum.ar => "Arabic",
            LangEnum.hi => "Hindi",
            LangEnum.mn_cy => "Mongolian",
            LangEnum.mn_mo => "Mongolian",
            LangEnum.km => "Central Khmer",
            LangEnum.nb_no => "Norwegian Bokmål",
            LangEnum.nn_no => "Norwegian Nynorsk",
            LangEnum.fa => "Persian",
            LangEnum.sv => "Swedish",
            LangEnum.pl => "Polish",
            LangEnum.nl => "Dutch",
            LangEnum.uk => "Ukrainian",
            _ => "auto"
        };
    }

    #endregion Interface Implementation

    #region Support

    /// <summary>
    ///     AWS Signature Version 4 签名
    ///     https://docs.aws.amazon.com/general/latest/gr/sigv4-signing-and-authentication.html
    /// </summary>
    private static Dictionary<string, string> BuildSigV4Headers(string host, string canonicalUri, string body,
        string region, string accessKeyId, string secretAccessKey)
    {
        const string service = "bedrock";

        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMdd\\THHmmss\\Z", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        const string canonicalQueryString = "";
        var canonicalHeaders = $"host:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";

        var canonicalRequest =
            $"POST\n{canonicalUri}\n{canonicalQueryString}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        var credentialScope = $"{dateStamp}/{region}/{service}/aws4_request";
        var hashedCanonicalRequest =
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))).ToLowerInvariant();
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{credentialScope}\n{hashedCanonicalRequest}";

        var kDate = HmacSha256(Encoding.UTF8.GetBytes($"AWS4{secretAccessKey}"), dateStamp);
        var kRegion = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, service);
        var kSigning = HmacSha256(kService, "aws4_request");
        var signature = Convert.ToHexString(HmacSha256(kSigning, stringToSign)).ToLowerInvariant();

        var authorizationHeader =
            $"AWS4-HMAC-SHA256 Credential={accessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        return new Dictionary<string, string>
        {
            { "x-amz-date", amzDate },
            { "x-amz-content-sha256", payloadHash },
            { "Authorization", authorizationHeader }
        };
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    /// <summary>
    ///     按照 AWS SigV4 的 URI 编码规则对 ModelId 做路径段编码 (未保留字符外全部转义, 含 '/' 与 ':')
    /// </summary>
    private static string AwsUriEncode(string input)
    {
        var sb = new StringBuilder();
        foreach (var ch in input)
        {
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') ||
                ch is '-' or '_' or '.' or '~')
                sb.Append(ch);
            else
                sb.Append('%').Append(((byte)ch).ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    #endregion
}
