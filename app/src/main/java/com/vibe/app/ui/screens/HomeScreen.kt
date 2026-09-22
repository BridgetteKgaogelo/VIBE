@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)

package com.vibe.app.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import com.vibe.app.R
import com.vibe.app.core.ConnectivityObserver
import com.vibe.app.data.repository.AuthRepository
import com.vibe.app.data.repository.GroupRepository
import com.vibe.app.data.repository.NotificationRepository
import com.vibe.app.data.sync.SyncManager
import com.vibe.app.domain.Group
import com.vibe.app.ui.components.EmptyState
import com.vibe.app.ui.components.GroupRow
import com.vibe.app.ui.components.NotificationBell
import com.vibe.app.ui.components.OfflineBanner
import com.vibe.app.ui.components.PrimaryButton
import com.vibe.app.ui.components.SecondaryButton
import com.vibe.app.ui.components.SectionHeader
import com.vibe.app.ui.components.VibeBottomBar
import com.vibe.app.ui.components.VibeCard
import com.vibe.app.ui.components.VibeTab
import com.vibe.app.ui.components.Wordmark
import com.vibe.app.ui.theme.VibeCoral
import com.vibe.app.ui.theme.VibeLilac
import com.vibe.app.ui.theme.VibeMint
import com.vibe.app.ui.vibeViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

class HomeViewModel(
    groupRepository: GroupRepository,
    authRepository: AuthRepository,
    notificationRepository: NotificationRepository,
    syncManager: SyncManager,
    connectivity: ConnectivityObserver,
) : ViewModel() {

    val groups: StateFlow<List<Group>> = groupRepository.groups
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val displayName: StateFlow<String> = authRepository.profile
        .map { user -> user?.displayName.orEmpty() }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), "")

    val unreadNotifications: StateFlow<Int> = notificationRepository.unreadCount
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), 0)

    val pendingSync: StateFlow<Int> = syncManager.pendingCount
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), 0)

    val offline: StateFlow<Boolean> = connectivity.isOnline
        .map { online -> !online }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), false)

    init {
        viewModelScope.launch {
            // The cache paints first; the refresh brings the group list up to date.
            groupRepository.refresh()
            notificationRepository.refresh()
        }
    }
}

@Composable
fun HomeScreen(
    onOpenGroup: (String) -> Unit,
    onOpenGroups: () -> Unit,
    onOpenActivities: () -> Unit,
    onOpenNotifications: () -> Unit,
    onCreateGroup: () -> Unit,
    onJoinGroup: () -> Unit,
    onStartDecision: (String) -> Unit,
    onDecideForUs: () -> Unit,
    onSurpriseMe: (String) -> Unit,
    onMemories: () -> Unit,
    onProfile: () -> Unit,
) {
    val viewModel = vibeViewModel {
        HomeViewModel(
            it.groupRepository,
            it.authRepository,
            it.notificationRepository,
            it.syncManager,
            it.connectivity,
        )
    }

    val groups by viewModel.groups.collectAsStateWithLifecycle()
    val name by viewModel.displayName.collectAsStateWithLifecycle()
    val unread by viewModel.unreadNotifications.collectAsStateWithLifecycle()
    val pending by viewModel.pendingSync.collectAsStateWithLifecycle()
    val offline by viewModel.offline.collectAsStateWithLifecycle()

    var quickActionsOpen by remember { mutableStateOf(false) }

    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        bottomBar = {
            VibeBottomBar(
                selected = VibeTab.HOME,
                onSelect = { tab ->
                    when (tab) {
                        VibeTab.HOME -> Unit
                        VibeTab.MEMORIES -> onMemories()
                        VibeTab.PROFILE -> onProfile()
                    }
                },
            )
        },
        floatingActionButton = {
            FloatingActionButton(
                onClick = { quickActionsOpen = true },
                containerColor = VibeCoral,
                contentColor = Color.White,
            ) {
                Icon(Icons.Filled.Add, contentDescription = stringResource(R.string.cd_quick_add))
            }
        },
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(horizontal = 16.dp),
        ) {
            item {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = stringResource(R.string.home_greeting, name.ifBlank { "Viber" }),
                            style = MaterialTheme.typography.headlineMedium,
                        )
                        Text(
                            text = stringResource(R.string.home_subtitle),
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    Wordmark(modifier = Modifier.padding(end = 4.dp))
                    NotificationBell(unreadCount = unread, onClick = onOpenNotifications)
                }
            }

            item { OfflineBanner(pendingCount = pending, offline = offline) }

            item {
                VibeCard(
                    containerColor = VibeCoral,
                    onClick = onDecideForUs,
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(text = "🗳️", fontSize = 28.sp, modifier = Modifier.padding(end = 12.dp))
                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = stringResource(R.string.home_decide_cta),
                                style = MaterialTheme.typography.titleLarge,
                                color = Color.White,
                            )
                            Text(
                                text = stringResource(R.string.home_decide_cta_body),
                                style = MaterialTheme.typography.bodyMedium,
                                color = Color.White.copy(alpha = 0.9f),
                            )
                        }
                        Text(text = "›", fontSize = 26.sp, color = Color.White)
                    }
                }
            }

            item { SectionHeader(stringResource(R.string.home_your_groups), stringResource(R.string.home_view_all), onOpenGroups) }

            if (groups.isEmpty()) {
                item {
                    EmptyState(
                        title = stringResource(R.string.home_empty_title),
                        body = stringResource(R.string.home_empty_body),
                    ) {
                        Column {
                            PrimaryButton(
                                text = stringResource(R.string.action_create_group),
                                onClick = onCreateGroup,
                            )
                            Spacer(modifier = Modifier.height(8.dp))
                            SecondaryButton(
                                text = stringResource(R.string.action_join_group),
                                onClick = onJoinGroup,
                            )
                        }
                    }
                }
            } else {
                items(groups, key = { it.id }) { group ->
                    GroupRow(
                        icon = group.icon,
                        name = group.name,
                        detail = pluralStringResource(
                            R.plurals.group_members,
                            group.memberCount,
                            group.memberCount,
                        ) + " · " + pluralStringResource(
                            R.plurals.activity_count,
                            group.activityCount,
                            group.activityCount,
                        ),
                        onClick = { onOpenGroup(group.id) },
                    )
                }
            }

            item {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    SecondaryButton(
                        text = stringResource(R.string.action_create_group),
                        modifier = Modifier.weight(1f),
                        onClick = onCreateGroup,
                    )
                    SecondaryButton(
                        text = stringResource(R.string.action_join_group),
                        modifier = Modifier.weight(1f),
                        onClick = onJoinGroup,
                    )
                }
            }

            item { SectionHeader(stringResource(R.string.home_quick_start)) }

            item {
                QuickStartCard(
                    icon = "💗",
                    title = stringResource(R.string.quick_decide_title),
                    body = stringResource(R.string.quick_decide_body),
                    accent = VibeLilac,
                    onClick = onDecideForUs,
                )
            }
            item {
                QuickStartCard(
                    icon = "🎲",
                    title = stringResource(R.string.quick_surprise_title),
                    body = stringResource(R.string.quick_surprise_body),
                    accent = VibeMint,
                    onClick = { groups.firstOrNull()?.let { onSurpriseMe(it.id) } },
                )
            }
            item {
                QuickStartCard(
                    icon = "📋",
                    title = stringResource(R.string.quick_activities_title),
                    body = stringResource(R.string.quick_activities_body),
                    accent = VibeCoral,
                    onClick = onOpenActivities,
                )
            }

            item { Spacer(modifier = Modifier.height(24.dp)) }
        }
    }

    if (quickActionsOpen) {
        AlertDialog(
            onDismissRequest = { quickActionsOpen = false },
            title = { Text(stringResource(R.string.quick_actions)) },
            text = {
                Column {
                    QuickActionRow("➕", stringResource(R.string.quick_add_title)) {
                        quickActionsOpen = false
                        onOpenActivities()
                    }
                    QuickActionRow("👥", stringResource(R.string.quick_create_group_title)) {
                        quickActionsOpen = false
                        onCreateGroup()
                    }
                    QuickActionRow("🗳️", stringResource(R.string.quick_start_decision_title)) {
                        quickActionsOpen = false
                        groups.firstOrNull()?.let { onStartDecision(it.id) }
                    }
                    QuickActionRow("🎲", stringResource(R.string.quick_surprise_title)) {
                        quickActionsOpen = false
                        groups.firstOrNull()?.let { onSurpriseMe(it.id) }
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = { quickActionsOpen = false }) {
                    Text(stringResource(R.string.action_close))
                }
            },
        )
    }
}

@Composable
private fun QuickStartCard(
    icon: String,
    title: String,
    body: String,
    accent: Color,
    onClick: () -> Unit,
) {
    VibeCard(modifier = Modifier.padding(bottom = 8.dp), onClick = onClick) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(text = icon, fontSize = 26.sp, modifier = Modifier.padding(end = 12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(text = title, style = MaterialTheme.typography.titleMedium, color = accent)
                Text(
                    text = body,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Text(text = "›", fontSize = 24.sp, color = accent)
        }
    }
}

@Composable
private fun QuickActionRow(icon: String, label: String, onClick: () -> Unit) {
    TextButton(onClick = onClick, modifier = Modifier.fillMaxWidth()) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(text = icon, fontSize = 20.sp, modifier = Modifier.padding(end = 10.dp))
            Text(text = label, color = MaterialTheme.colorScheme.onSurface)
        }
    }
}
