using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Signup.Services;

/// <summary>
/// File Transformation callbacks for Jellyfin web login assets.
/// </summary>
public static partial class SignupLoginPageTransformation
{
    private const string SignupButtonHtml = """

            <button is="emby-button" type="button" class="raised button-submit block btnSignup hide">
                <span>Create Account</span>
            </button>
""";

    private const string LoginJsHelpers = """

function getJellyfinSignupTargetUrl(apiClient, targetUrl) {
    const cleaned = (targetUrl || '').trim();

    if (!cleaned) {
        return apiClient.getUrl('signup.html');
    }

    if (/^https?:\/\//i.test(cleaned)) {
        return cleaned;
    }

    return apiClient.getUrl(cleaned.replace(/^\/+/, ''));
}

function getJellyfinSignupPasswordResetUrl(apiClient) {
    const signupUrl = apiClient.getUrl('signup.html');
    return `${signupUrl}${signupUrl.includes('?') ? '&' : '?'}reset=1`;
}

function loadJellyfinSignupButton(context, apiClient) {
    const button = context.querySelector('.btnSignup');

    if (!button) {
        return Promise.resolve();
    }

    return apiClient.getJSON(apiClient.getUrl('Signup/v1/public/login-button')).then(settings => {
        if (!settings?.enabled) {
            button.classList.add('hide');
            button.dataset.signupUrl = '';
            return;
        }

        button.querySelector('span').textContent = settings.text || 'Create Account';
        button.dataset.signupUrl = getJellyfinSignupTargetUrl(apiClient, settings.targetUrl);
        button.classList.remove('hide');
    }).catch(() => {
        button.classList.add('hide');
        button.dataset.signupUrl = '';
    });
}
""";

    private const string LoginBundlePatch = """

;(() => {
    const patchMarker = 'jellyfin-signup-login-native-patch-v4';

    if (window[patchMarker]) {
        return;
    }

    window[patchMarker] = true;

    function getBasePath() {
        return window.location.pathname
            .replace(/\/web\/?.*$/i, '')
            .replace(/\/$/, '');
    }

    function normalizeUrl(path) {
        const cleaned = (path || '').replace(/^\/+/, '');
        const base = `${window.location.origin}${getBasePath()}`;
        return `${base}/${cleaned}`.replace(/([^:]\/)\/+/g, '$1');
    }

    function getPluginUrl(path) {
        if (window.ApiClient?.getUrl) {
            return window.ApiClient.getUrl(path);
        }

        return normalizeUrl(path);
    }

    function getSignupUrl(targetUrl) {
        const cleaned = (targetUrl || '').trim();

        if (!cleaned) {
            return getPluginUrl('signup.html');
        }

        if (/^https?:\/\//i.test(cleaned)) {
            return cleaned;
        }

        return getPluginUrl(cleaned.replace(/^\/+/, ''));
    }

    function getPasswordResetUrl() {
        const signupUrl = getPluginUrl('signup.html');
        return `${signupUrl}${signupUrl.includes('?') ? '&' : '?'}reset=1`;
    }

    function setButtonText(button, text) {
        const label = button.querySelector('span') || button;
        label.textContent = text || 'Create Account';
    }

    function ensureStyle() {
        if (document.getElementById('jellyfin-signup-login-style')) {
            return;
        }

        const style = document.createElement('style');
        style.id = 'jellyfin-signup-login-style';
        style.textContent = `
            #loginPage .btnSignup.hide { display: none !important; }
            #loginPage .btnSignup { margin-top: .5em; }
        `;
        document.head.appendChild(style);
    }

    async function loadSignupButton(button) {
        try {
            const response = await fetch(getPluginUrl('Signup/v1/public/login-button'), {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            });

            if (!response.ok) {
                throw new Error(`Signup button request failed with ${response.status}`);
            }

            const settings = await response.json();

            if (!settings?.enabled) {
                button.classList.add('hide');
                button.dataset.signupUrl = '';
                return;
            }

            setButtonText(button, settings.text);
            button.dataset.signupUrl = getSignupUrl(settings.targetUrl);
            button.classList.remove('hide');
        } catch {
            button.dataset.signupUrl = getSignupUrl();
            button.classList.remove('hide');
        }
    }

    function ensureSignupButton(container, forgotPasswordButton) {
        let signupButton = container.querySelector('.btnSignup');

        if (!signupButton) {
            forgotPasswordButton.insertAdjacentHTML(
                'beforebegin',
                '<button is="emby-button" type="button" class="raised button-submit block btnSignup emby-button" data-signup-url="' + getSignupUrl() + '"><span>Create Account</span></button>');
            signupButton = container.querySelector('.btnSignup');
        }

        if (!signupButton) {
            return;
        }

        if (signupButton.dataset.jellyfinSignupBound !== 'true') {
            signupButton.dataset.jellyfinSignupBound = 'true';
            signupButton.addEventListener('click', event => {
                event.preventDefault();
                event.stopPropagation();

                const targetUrl = signupButton.dataset.signupUrl || getSignupUrl();
                window.location.href = targetUrl;
            });
        }

        if (signupButton.dataset.jellyfinSignupLoaded !== 'true') {
            signupButton.dataset.jellyfinSignupLoaded = 'true';
            loadSignupButton(signupButton);
        }
    }

    function patchForgotPassword(forgotPasswordButton) {
        if (forgotPasswordButton.dataset.jellyfinSignupResetBound === 'true') {
            return;
        }

        forgotPasswordButton.dataset.jellyfinSignupResetBound = 'true';
        forgotPasswordButton.addEventListener('click', event => {
            event.preventDefault();
            event.stopImmediatePropagation();
            window.location.href = getPasswordResetUrl();
        }, true);
    }

    function patchLoginPage() {
        const loginPage = document.querySelector('#loginPage');

        if (!loginPage) {
            return;
        }

        const forgotPasswordButton = loginPage.querySelector('.btnForgotPassword');
        const container = forgotPasswordButton?.parentElement || loginPage.querySelector('.readOnlyContent');

        if (!container || !forgotPasswordButton) {
            return;
        }

        ensureStyle();
        try {
            ensureSignupButton(container, forgotPasswordButton);
            patchForgotPassword(forgotPasswordButton);
        } catch {
            // Keep the observer alive if Jellyfin re-renders the login form mid-patch.
        }
    }

    const observer = new MutationObserver(patchLoginPage);
    observer.observe(document.documentElement, { childList: true, subtree: true });

    document.addEventListener('DOMContentLoaded', patchLoginPage);
    document.addEventListener('viewshow', patchLoginPage, true);
    window.addEventListener('hashchange', patchLoginPage);
    window.setInterval(patchLoginPage, 1000);
    patchLoginPage();
})();
""";

    /// <summary>
    /// Patches the Jellyfin login HTML.
    /// </summary>
    /// <param name="payload">Transformation payload.</param>
    /// <returns>Patched contents.</returns>
    public static string LoginHtml(SignupFileTransformationPayload payload)
    {
        var contents = payload.Contents ?? string.Empty;

        if (contents.Contains("btnSignup", StringComparison.Ordinal))
        {
            return contents;
        }

        return ForgotPasswordButtonRegex().Replace(
            contents,
            match => $"{SignupButtonHtml}{match.Value}",
            1);
    }

    /// <summary>
    /// Patches the Jellyfin login JavaScript.
    /// </summary>
    /// <param name="payload">Transformation payload.</param>
    /// <returns>Patched contents.</returns>
    public static string LoginJs(SignupFileTransformationPayload payload)
    {
        var contents = payload.Contents ?? string.Empty;

        contents = EnsureHelpers(contents);
        contents = ReplaceForgotPasswordHandler(contents);
        contents = RemoveLegacyDisclaimerSignupSuppression(contents);
        contents = EnsureSignupButtonHandler(contents);
        contents = EnsureSignupButtonLoad(contents);

        return contents;
    }

    /// <summary>
    /// Patches the bundled Jellyfin web app login runtime.
    /// </summary>
    /// <param name="payload">Transformation payload.</param>
    /// <returns>Patched contents.</returns>
    public static string LoginBundleJs(SignupFileTransformationPayload payload)
    {
        var contents = payload.Contents ?? string.Empty;

        if (contents.Contains("jellyfin-signup-login-native-patch-v4", StringComparison.Ordinal))
        {
            return contents;
        }

        return $"{contents}{Environment.NewLine}{LoginBundlePatch}";
    }

    private static string EnsureHelpers(string contents)
    {
        if (contents.Contains("function getJellyfinSignupPasswordResetUrl(", StringComparison.Ordinal))
        {
            return contents;
        }

        var markerIndex = contents.IndexOf("function loadUserList", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return contents.Insert(markerIndex, LoginJsHelpers);
        }

        var exportIndex = contents.IndexOf("export default function", StringComparison.Ordinal);
        return exportIndex >= 0
            ? contents.Insert(exportIndex, LoginJsHelpers)
            : $"{LoginJsHelpers}{Environment.NewLine}{contents}";
    }

    private static string ReplaceForgotPasswordHandler(string contents)
    {
        return contents.Replace(
            "Dashboard.navigate('forgotpassword');",
            "window.location.href = getJellyfinSignupPasswordResetUrl(getApiClient());",
            StringComparison.Ordinal);
    }

    private static string RemoveLegacyDisclaimerSignupSuppression(string contents)
    {
        return LegacyDisclaimerSignupSuppressionRegex().Replace(
            contents,
            """

            for (const elem of loginDisclaimer.querySelectorAll('.jellyfin-signup-login-button')) {
                elem.remove();
            }
""",
            1);
    }

    private static string EnsureSignupButtonHandler(string contents)
    {
        if (contents.Contains(".btnSignup').addEventListener", StringComparison.Ordinal))
        {
            return contents;
        }

        var marker = """
    view.querySelector('.btnForgotPassword').addEventListener('click', function () {
        window.location.href = getJellyfinSignupPasswordResetUrl(getApiClient());
    });
""";
        var handler = """
    const jellyfinSignupButton = view.querySelector('.btnSignup');
    if (jellyfinSignupButton) {
        jellyfinSignupButton.addEventListener('click', function () {
            window.location.href = jellyfinSignupButton.dataset.signupUrl || getJellyfinSignupTargetUrl(getApiClient());
        });
    }
""";
        var markerIndex = contents.IndexOf(marker, StringComparison.Ordinal);

        return markerIndex >= 0
            ? contents.Insert(markerIndex + marker.Length, $"{Environment.NewLine}{handler}")
            : contents;
    }

    private static string EnsureSignupButtonLoad(string contents)
    {
        if (contents.Contains("loadSignupButton(view, apiClient)", StringComparison.Ordinal)
            || contents.Contains("loadJellyfinSignupButton(view, apiClient)", StringComparison.Ordinal))
        {
            return contents;
        }

        var marker = "        const apiClient = getApiClient();";
        var markerIndex = contents.IndexOf(marker, StringComparison.Ordinal);

        return markerIndex >= 0
            ? contents.Insert(markerIndex + marker.Length, $"{Environment.NewLine}{Environment.NewLine}        loadJellyfinSignupButton(view, apiClient);")
            : contents;
    }

    [GeneratedRegex("""\s*<button is="emby-button" type="button" class="raised cancel block btnForgotPassword">""", RegexOptions.CultureInvariant)]
    private static partial Regex ForgotPasswordButtonRegex();

    [GeneratedRegex("""\s*if\s*\(\s*loginDisclaimer\.querySelector\('\.jellyfin-signup-login-button'\)\s*\)\s*\{\s*view\.querySelector\('\.btnSignup'\)\.classList\.add\('hide'\);\s*\}""", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyDisclaimerSignupSuppressionRegex();
}
