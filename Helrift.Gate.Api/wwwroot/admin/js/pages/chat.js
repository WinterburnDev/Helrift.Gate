(function () {
    const { $, api } = Admin;

    window.openDrawer_broadcast = function () {
        Admin.openDrawer(
            'System Broadcast',
            `<p style="font-size:13px; color:var(--muted); margin:0 0 16px;">Send a message to all players across all connected game servers.</p>
            <div class="form-grid">
                <div class="field" style="grid-column:1/-1"><label>Message</label><textarea id="dChatMessage" rows="4" placeholder="Type a system broadcast message…"></textarea></div>
                <div class="field"><label>Chat Type</label><select id="dChatType"><option value="System">System</option><option value="Announcement">Announcement</option></select></div>
                <div class="field"><label>Sender Name</label><input id="dChatSender" type="text" value="System" /></div>
            </div>`,
            'Send Broadcast',
            doSendBroadcast,
            'btn-primary'
        );
    };

    async function doSendBroadcast() {
        const message = $('dChatMessage').value.trim();
        const chatType = $('dChatType').value;
        const senderName = $('dChatSender').value.trim() || 'System';
        const st = $('drawerStatus');
        st.textContent = '';

        if (!message) { st.textContent = 'Message is required.'; return; }

        try {
            const res = await api('/api/v1/chat/broadcast', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    senderId: 0,
                    chatType,
                    characterName: senderName,
                    message,
                    side: '',
                    partyId: '',
                    guildId: '',
                    originServerId: 'gate-admin',
                    isAdmin: true
                })
            });
            if (res.ok) { Admin.closeDrawer(); }
            else { st.textContent = `Error ${res.status}`; }
        } catch (e) { st.textContent = 'Network error.'; console.error(e); }
    }

    Admin.registerPage('chat', {});
})();