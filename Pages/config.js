export default function (view) {
    const appearanceDefaults = {
        backgroundColor: '#08070d',
        heading: 'Jellyfin Signup',
        introText: 'Create your Jellyfin account, then verify your email before signing in.',
        logoUrl: '',
        mutedTextColor: '#bbb6c8',
        pageTitle: 'Jellyfin Signup',
        panelColor: '#15131d',
        primaryColor: '#00a4dc',
        secondaryColor: '#aa5cc3',
        textColor: '#f7f7fb'
    };
    const state = {
        createdInviteCode: null,
        invites: null,
        settings: null
    };
    const managedButtonStart = '<!-- jellyfin-signup-button:start -->';
    const managedButtonEnd = '<!-- jellyfin-signup-button:end -->';

    const $ = selector => view.querySelector(selector);

    function setMessage(selector, message, isError) {
        const target = $(selector);
        if (!target) {
            return;
        }

        target.textContent = message || '';
        target.classList.toggle('is-error', !!isError);
    }

    function splitIds(value) {
        return (value || '')
            .split(',')
            .map(item => item.trim())
            .filter(Boolean);
    }

    function joinIds(values) {
        return (values || []).join(', ');
    }

    function publicSignupUrl() {
        return ApiClient.getUrl('signup.html');
    }

    function escapeHtml(value) {
        return String(value || '').replace(/[&<>"']/g, character => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        }[character]));
    }

    function escapeRegex(value) {
        return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    function normalizeHex(value, fallback) {
        const cleaned = String(value || '').trim();
        return /^#[0-9a-f]{6}$/i.test(cleaned) ? cleaned.toLowerCase() : fallback;
    }

    function resolveLoginButtonTarget(targetUrl) {
        const cleaned = (targetUrl || '').trim();

        if (!cleaned) {
            return publicSignupUrl();
        }

        if (/^https?:\/\//i.test(cleaned)) {
            return cleaned;
        }

        return ApiClient.getUrl(cleaned.replace(/^\/+/, ''));
    }

    function removeManagedSignupButton(disclaimer) {
        const pattern = new RegExp(`${escapeRegex(managedButtonStart)}[\\s\\S]*?${escapeRegex(managedButtonEnd)}`, 'g');
        return String(disclaimer || '').replace(pattern, '').trim();
    }

    function buildManagedSignupButton(settings) {
        const loginButton = settings.loginButtonSettings || {};
        const targetUrl = resolveLoginButtonTarget(loginButton.targetUrl);
        const buttonText = loginButton.text || 'Create Account';

        return `${managedButtonStart}
<div class="jellyfin-signup-login-button" style="margin:.75em auto 0;max-width:20em;text-align:center;">
    <a href="${escapeHtml(targetUrl)}" class="raised button-submit block">${escapeHtml(buttonText)}</a>
</div>
${managedButtonEnd}`;
    }

    async function syncBrandingSignupButton(settings) {
        const loginButton = settings.loginButtonSettings || {};
        const shouldShow = !!settings.enabled && loginButton.enabled !== false;
        const branding = await ApiClient.getNamedConfiguration('branding');
        const withoutManagedButton = removeManagedSignupButton(branding?.LoginDisclaimer);
        const nextDisclaimer = shouldShow
            ? [withoutManagedButton, buildManagedSignupButton(settings)].filter(Boolean).join('\n\n')
            : withoutManagedButton;

        if ((branding?.LoginDisclaimer || '') === nextDisclaimer) {
            return;
        }

        await ApiClient.updateNamedConfiguration('branding', {
            ...(branding || {}),
            LoginDisclaimer: nextDisclaimer
        });
    }

    function request(method, path, body) {
        return ApiClient.ajax({
            type: method,
            url: ApiClient.getUrl(path),
            dataType: method === 'DELETE' ? undefined : 'json',
            contentType: body ? 'application/json' : undefined,
            data: body ? JSON.stringify(body) : undefined
        });
    }

    function setOptions(select, presets) {
        select.innerHTML = '';
        presets.forEach(preset => {
            const option = document.createElement('option');
            option.value = preset.id;
            option.textContent = preset.name;
            select.appendChild(option);
        });
    }

    function setStatus(selector, value, stateClass) {
        const card = $(selector);
        card.classList.remove('is-on', 'is-warn');
        if (stateClass) {
            card.classList.add(stateClass);
        }

        card.querySelector('.jfs-status-value').textContent = value;
    }

    function updateStatuses() {
        const settings = state.settings || {};
        const email = settings.emailSettings || {};
        const loginButton = settings.loginButtonSettings || {};
        const invites = state.invites?.invites || [];
        const activeInvites = invites.filter(invite => invite.usable).length;

        setStatus('#statusSignup', settings.enabled ? 'Enabled' : 'Disabled', settings.enabled ? 'is-on' : 'is-warn');
        setStatus('#statusInvites', settings.requireInvite === false ? 'Optional' : `${activeInvites} Active`, settings.requireInvite === false || activeInvites ? 'is-on' : 'is-warn');
        setStatus('#statusLoginButton', settings.enabled && loginButton.enabled ? 'Visible' : 'Hidden', settings.enabled && loginButton.enabled ? 'is-on' : 'is-warn');
        setStatus('#statusEmail', email.enabled ? 'Enabled' : 'Disabled', email.enabled ? 'is-on' : 'is-warn');
    }

    function appearanceSettings() {
        return {
            ...appearanceDefaults,
            ...(state.settings?.appearanceSettings || {})
        };
    }

    function hydrateAppearance() {
        const appearance = appearanceSettings();

        $('#appearancePageTitle').value = appearance.pageTitle || appearanceDefaults.pageTitle;
        $('#appearanceHeading').value = appearance.heading || appearanceDefaults.heading;
        $('#appearanceIntroText').value = appearance.introText || appearanceDefaults.introText;
        $('#appearanceLogoUrl').value = appearance.logoUrl || '';
        $('#appearanceBackgroundColor').value = normalizeHex(appearance.backgroundColor, appearanceDefaults.backgroundColor);
        $('#appearancePanelColor').value = normalizeHex(appearance.panelColor, appearanceDefaults.panelColor);
        $('#appearanceTextColor').value = normalizeHex(appearance.textColor, appearanceDefaults.textColor);
        $('#appearanceMutedTextColor').value = normalizeHex(appearance.mutedTextColor, appearanceDefaults.mutedTextColor);
        $('#appearancePrimaryColor').value = normalizeHex(appearance.primaryColor, appearanceDefaults.primaryColor);
        $('#appearanceSecondaryColor').value = normalizeHex(appearance.secondaryColor, appearanceDefaults.secondaryColor);
        updateAppearancePreview();
    }

    function collectAppearanceSettings() {
        return {
            backgroundColor: normalizeHex($('#appearanceBackgroundColor').value, appearanceDefaults.backgroundColor),
            heading: $('#appearanceHeading').value,
            introText: $('#appearanceIntroText').value,
            logoUrl: $('#appearanceLogoUrl').value,
            mutedTextColor: normalizeHex($('#appearanceMutedTextColor').value, appearanceDefaults.mutedTextColor),
            pageTitle: $('#appearancePageTitle').value,
            panelColor: normalizeHex($('#appearancePanelColor').value, appearanceDefaults.panelColor),
            primaryColor: normalizeHex($('#appearancePrimaryColor').value, appearanceDefaults.primaryColor),
            secondaryColor: normalizeHex($('#appearanceSecondaryColor').value, appearanceDefaults.secondaryColor),
            textColor: normalizeHex($('#appearanceTextColor').value, appearanceDefaults.textColor)
        };
    }

    function updateAppearancePreview() {
        const appearance = collectAppearanceSettings();
        const preview = $('#appearancePreview');
        const logo = $('#appearancePreviewLogo');
        const mark = $('#appearancePreviewMark');

        preview.style.setProperty('--preview-bg', appearance.backgroundColor);
        preview.style.setProperty('--preview-panel', appearance.panelColor);
        preview.style.setProperty('--preview-text', appearance.textColor);
        preview.style.setProperty('--preview-muted', appearance.mutedTextColor);
        preview.style.setProperty('--preview-primary', appearance.primaryColor);
        preview.style.setProperty('--preview-secondary', appearance.secondaryColor);
        $('#appearancePreviewHeading').textContent = appearance.heading || appearanceDefaults.heading;
        $('#appearancePreviewIntro').textContent = appearance.introText || appearanceDefaults.introText;

        if (appearance.logoUrl.trim()) {
            logo.src = appearance.logoUrl.trim();
            logo.hidden = false;
            mark.hidden = true;
        } else {
            logo.removeAttribute('src');
            logo.hidden = true;
            mark.hidden = false;
        }
    }

    function hydrateSettings() {
        const settings = state.settings;
        const email = settings.emailSettings || {};
        const loginButton = settings.loginButtonSettings || {};

        $('#signupEnabled').checked = !!settings.enabled;
        $('#requireInvite').checked = settings.requireInvite !== false;
        $('#publicSignupUrl').value = publicSignupUrl();
        $('#loginButtonEnabled').checked = loginButton.enabled !== false;
        $('#loginButtonText').value = loginButton.text || 'Create Account';
        $('#loginButtonTargetUrl').value = loginButton.targetUrl || '';
        $('#emailEnabled').checked = !!email.enabled;
        $('#smtpHost').value = email.smtpHost || '';
        $('#smtpPort').value = email.smtpPort || 587;
        $('#smtpSsl').checked = email.useSsl !== false;
        $('#smtpUsername').value = email.username || '';
        $('#smtpPassword').value = '';
        $('#fromAddress').value = email.fromAddress || '';
        $('#fromName').value = email.fromName || 'Jellyfin';
        $('#emailPublicSignupUrl').value = email.publicSignupUrl || publicSignupUrl();
        $('#verificationLinkMinutes').value = email.verificationLinkMinutes || 30;
        $('#resetCodeMinutes').value = email.resetCodeMinutes || 15;
        $('#defaultEnabledFolderIds').value = joinIds(settings.defaultEnabledFolderIds);

        setOptions($('#defaultPolicyPresetId'), settings.policyPresets || []);
        setOptions($('#invitePolicyPresetId'), settings.policyPresets || []);
        $('#defaultPolicyPresetId').value = settings.defaultPolicyPresetId || 'standard';
        $('#invitePolicyPresetId').value = settings.defaultPolicyPresetId || 'standard';
        hydrateAppearance();
        renderUserEmails();
        updateStatuses();
    }

    function createInviteMeta(invite) {
        const parts = [
            invite.codePreview ? `code ends with ${invite.codePreview}` : 'code hidden',
            `${invite.usedCount}/${invite.maxUses || 'unlimited'} uses`
        ];

        if (invite.emailRequired) {
            parts.push('email required');
        }

        if (invite.expiresAtUtc) {
            parts.push(`expires ${new Date(invite.expiresAtUtc).toLocaleString()}`);
        }

        return parts.join(' - ');
    }

    async function copyText(value, messageSelector, successMessage) {
        try {
            await navigator.clipboard.writeText(value);
        } catch {
            const textarea = document.createElement('textarea');
            textarea.value = value;
            textarea.setAttribute('readonly', '');
            textarea.style.position = 'fixed';
            textarea.style.left = '-9999px';
            document.body.appendChild(textarea);
            textarea.select();
            document.execCommand('copy');
            textarea.remove();
        }

        setMessage(messageSelector, successMessage);
    }

    function renderCreatedInvite() {
        const target = $('#createdInvite');
        const code = state.createdInviteCode;

        target.innerHTML = '';
        target.classList.remove('is-error');

        if (!code) {
            return;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'jfs-created-code';

        const details = document.createElement('div');

        const note = document.createElement('div');
        note.className = 'jfs-field-note';
        note.textContent = 'Invite code. Copy it now; it cannot be shown again after you leave this page.';

        const codeElement = document.createElement('code');
        codeElement.textContent = code;

        details.appendChild(note);
        details.appendChild(codeElement);

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'raised cancel';
        button.innerHTML = '<span>Copy</span>';
        button.addEventListener('click', () => {
            copyText(code, '#settingsMessage', 'Invite code copied.').catch(err => {
                setMessage('#settingsMessage', err?.message || 'Unable to copy invite code.', true);
            });
        });

        wrapper.appendChild(details);
        wrapper.appendChild(button);
        target.appendChild(wrapper);
    }

    function renderInvites() {
        const list = $('#inviteList');
        const invites = state.invites?.invites || [];

        if (!invites.length) {
            list.innerHTML = '<div class="jfs-empty">No invite codes have been created yet.</div>';
            return;
        }

        list.innerHTML = '';
        invites.forEach(invite => {
            const row = document.createElement('div');
            row.className = 'jfs-invite-row';

            const body = document.createElement('div');

            const title = document.createElement('div');
            title.className = 'jfs-invite-title';

            const label = document.createElement('span');
            label.textContent = invite.label || 'Invite';
            title.appendChild(label);

            const status = document.createElement('span');
            status.className = `jfs-pill ${invite.disabled ? 'is-disabled' : invite.usable ? 'is-active' : ''}`;
            status.textContent = invite.disabled ? 'Disabled' : invite.usable ? 'Active' : 'Unavailable';
            title.appendChild(status);

            const meta = document.createElement('div');
            meta.className = 'jfs-field-note';
            meta.textContent = createInviteMeta(invite);

            body.appendChild(title);
            body.appendChild(meta);

            const actions = document.createElement('div');
            actions.className = 'jfs-row-actions';
            actions.innerHTML = `
                <button is="emby-button" type="button" class="raised cancel btnDisableInvite"><span>Disable</span></button>
                <button is="emby-button" type="button" class="raised cancel jfs-danger btnDeleteInvite"><span>Delete</span></button>
            `;

            const disableButton = actions.querySelector('.btnDisableInvite');
            disableButton.disabled = !!invite.disabled;
            disableButton.addEventListener('click', async () => {
                await request('POST', `Signup/v1/admin/invites/${encodeURIComponent(invite.id)}/disable`);
                await load();
            });

            const deleteButton = actions.querySelector('.btnDeleteInvite');
            deleteButton.addEventListener('click', async () => {
                await request('DELETE', `Signup/v1/admin/invites/${encodeURIComponent(invite.id)}`);
                await load();
            });

            row.appendChild(body);
            row.appendChild(actions);
            list.appendChild(row);
        });
    }

    function renderUserEmails() {
        const list = $('#userEmailList');
        const users = state.settings?.users || [];
        const configuredCount = users.filter(user => user.emailConfigured || user.email).length;
        const resetCount = state.settings?.pendingResetCount || 0;
        const verificationCount = state.settings?.pendingVerificationCount || 0;

        $('#userEmailSummary').textContent = `${configuredCount}/${users.length} users mapped - ${resetCount} reset codes - ${verificationCount} pending verifications`;

        if (!users.length) {
            list.innerHTML = '<div class="jfs-empty">No Jellyfin users were found.</div>';
            return;
        }

        list.innerHTML = '';
        users.forEach(user => {
            const row = document.createElement('div');
            row.className = 'jfs-user-row';
            row.dataset.userId = user.userId || '';

            const identity = document.createElement('div');

            const name = document.createElement('div');
            name.className = 'jfs-user-name';
            name.textContent = user.username || 'Jellyfin user';

            const userId = document.createElement('div');
            userId.className = 'jfs-field-note jfs-user-id';
            userId.textContent = user.userId || 'Missing user id';

            identity.appendChild(name);
            identity.appendChild(userId);

            const emailContainer = document.createElement('div');
            emailContainer.className = 'inputContainer';

            const emailLabel = document.createElement('label');
            emailLabel.className = 'inputLabel inputLabelUnfocused';
            emailLabel.textContent = 'Email';

            const emailInput = document.createElement('input');
            emailInput.setAttribute('is', 'emby-input');
            emailInput.className = 'jfs-user-email';
            emailInput.type = 'email';
            emailInput.value = user.email || '';
            emailInput.placeholder = 'name@example.com';

            emailContainer.appendChild(emailLabel);
            emailContainer.appendChild(emailInput);

            const source = document.createElement('span');
            source.className = `jfs-pill ${user.emailConfigured || user.email ? 'is-active' : ''}`;
            source.textContent = user.source === 'signup'
                ? 'Signup'
                : user.source === 'admin'
                    ? 'Mapped'
                    : user.email || user.emailConfigured
                        ? 'Saved'
                        : 'No email';

            row.appendChild(identity);
            row.appendChild(emailContainer);
            row.appendChild(source);
            list.appendChild(row);
        });
    }

    function collectUserMappings() {
        return Array.from(view.querySelectorAll('.jfs-user-row')).map(row => ({
            email: row.querySelector('.jfs-user-email')?.value || '',
            userId: row.dataset.userId || ''
        }));
    }

    function selectTab(tabName) {
        view.querySelectorAll('[data-jfs-tab]').forEach(button => {
            button.setAttribute('aria-selected', String(button.dataset.jfsTab === tabName));
        });

        view.querySelectorAll('[data-jfs-panel]').forEach(panel => {
            panel.hidden = panel.dataset.jfsPanel !== tabName;
        });

        if (tabName === 'appearance') {
            updateAppearancePreview();
        }
    }

    async function load(options = {}) {
        setMessage('#settingsMessage', '');
        if (!options.preserveCreatedInvite) {
            state.createdInviteCode = null;
            renderCreatedInvite();
        }

        state.settings = await request('GET', 'Signup/v1/admin/settings');
        state.invites = await request('GET', 'Signup/v1/admin/invites');
        hydrateSettings();
        renderInvites();
    }

    view.querySelectorAll('[data-jfs-tab]').forEach(button => {
        button.addEventListener('click', () => selectTab(button.dataset.jfsTab));
    });

    ['appearancePageTitle', 'appearanceHeading', 'appearanceIntroText', 'appearanceLogoUrl',
        'appearanceBackgroundColor', 'appearancePanelColor', 'appearanceTextColor', 'appearanceMutedTextColor',
        'appearancePrimaryColor', 'appearanceSecondaryColor'].forEach(id => {
        $(`#${id}`).addEventListener('input', updateAppearancePreview);
    });

    view.querySelector('.jellyfin-signup-settings').addEventListener('submit', async event => {
        event.preventDefault();
        const emailSettings = {
            enabled: $('#emailEnabled').checked,
            smtpHost: $('#smtpHost').value,
            smtpPort: Number($('#smtpPort').value || 587),
            useSsl: $('#smtpSsl').checked,
            username: $('#smtpUsername').value,
            password: $('#smtpPassword').value,
            fromAddress: $('#fromAddress').value,
            fromName: $('#fromName').value,
            publicSignupUrl: $('#emailPublicSignupUrl').value,
            logoUrl: state.settings?.emailSettings?.logoUrl || 'https://jellyfin.org/images/logo.svg',
            resetCodeMinutes: Number($('#resetCodeMinutes').value || 15),
            resetSubject: state.settings?.emailSettings?.resetSubject || 'Your Jellyfin password reset code',
            verificationLinkMinutes: Number($('#verificationLinkMinutes').value || 30),
            verificationSubject: state.settings?.emailSettings?.verificationSubject || 'Verify your Jellyfin email'
        };

        state.settings = await request('POST', 'Signup/v1/admin/settings', {
            appearanceSettings: collectAppearanceSettings(),
            defaultEnabledFolderIds: splitIds($('#defaultEnabledFolderIds').value),
            defaultPolicyPresetId: $('#defaultPolicyPresetId').value,
            emailSettings,
            enabled: $('#signupEnabled').checked,
            loginButtonSettings: {
                enabled: $('#loginButtonEnabled').checked,
                targetUrl: $('#loginButtonTargetUrl').value,
                text: $('#loginButtonText').value
            },
            requireInvite: $('#requireInvite').checked,
            users: collectUserMappings()
        });

        try {
            await syncBrandingSignupButton(state.settings);
            setMessage('#settingsMessage', 'Settings saved. Login page button synced.');
        } catch (err) {
            setMessage('#settingsMessage', err?.message || 'Settings saved, but the login page button could not be synced.', true);
        }

        hydrateSettings();
    });

    view.querySelector('.jellyfin-signup-invite-form').addEventListener('submit', async event => {
        event.preventDefault();
        const expiresValue = $('#inviteExpiresAt').value;
        const invite = await request('POST', 'Signup/v1/admin/invites', {
            enabledFolderIds: splitIds($('#inviteEnabledFolderIds').value),
            emailRequired: $('#inviteEmailRequired').checked,
            expiresAtUtc: expiresValue ? new Date(expiresValue).toISOString() : null,
            label: $('#inviteLabel').value,
            maxUses: Number($('#inviteMaxUses').value || 1),
            policyPresetId: $('#invitePolicyPresetId').value
        });

        state.createdInviteCode = invite.code || '';
        renderCreatedInvite();
        await load({ preserveCreatedInvite: true });
    });

    $('#sendTestEmail').addEventListener('click', async () => {
        try {
            const result = await request('POST', 'Signup/v1/admin/email/test', {
                recipient: $('#testRecipient').value
            });
            setMessage('#settingsMessage', result.message || 'Test email sent.');
        } catch (err) {
            setMessage('#settingsMessage', err?.responseJSON?.error || err?.message || 'Test email failed.', true);
        }
    });

    $('#openPublicSignup').addEventListener('click', () => {
        window.open(publicSignupUrl(), '_blank', 'noopener,noreferrer');
    });

    $('#copyPublicSignupUrl').addEventListener('click', async () => {
        try {
            await copyText(publicSignupUrl(), '#settingsMessage', 'Signup URL copied.');
        } catch (err) {
            setMessage('#settingsMessage', err?.message || 'Unable to copy signup URL.', true);
        }
    });

    $('#refreshSignupSettings').addEventListener('click', () => {
        load().catch(err => {
            setMessage('#settingsMessage', err?.responseJSON?.error || err?.message || 'Unable to refresh signup settings.', true);
        });
    });

    load().catch(err => {
        setMessage('#settingsMessage', err?.responseJSON?.error || err?.message || 'Unable to load signup settings.', true);
    });
}
