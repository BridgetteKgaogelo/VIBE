package com.vibe.app.ui.navigation

import androidx.compose.runtime.Composable
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.navArgument
import com.vibe.app.ui.screens.ActivitiesScreen
import com.vibe.app.ui.screens.ActivityDetailScreen
import com.vibe.app.ui.screens.ActivityEditScreen
import com.vibe.app.ui.screens.ChangePasswordScreen
import com.vibe.app.ui.screens.CreateGroupScreen
import com.vibe.app.ui.screens.DecisionStartScreen
import com.vibe.app.ui.screens.EditProfileScreen
import com.vibe.app.ui.screens.GroupDetailScreen
import com.vibe.app.ui.screens.GroupsScreen
import com.vibe.app.ui.screens.HelpScreen
import com.vibe.app.ui.screens.HomeScreen
import com.vibe.app.ui.screens.JoinGroupScreen
import com.vibe.app.ui.screens.LanguageScreen
import com.vibe.app.ui.screens.LoginScreen
import com.vibe.app.ui.screens.MemoriesScreen
import com.vibe.app.ui.screens.MemoryDetailScreen
import com.vibe.app.ui.screens.NotificationsScreen
import com.vibe.app.ui.screens.OnboardingScreen
import com.vibe.app.ui.screens.PlanScreen
import com.vibe.app.ui.screens.ProfileScreen
import com.vibe.app.ui.screens.RegisterScreen
import com.vibe.app.ui.screens.SettingsScreen
import com.vibe.app.ui.screens.SurpriseScreen
import com.vibe.app.ui.screens.VoteScreen
import com.vibe.app.ui.screens.WinnerScreen

/**
 * Navigation flow (design document, section 4):
 *
 * Splash/onboarding -> Login/Register/Google SSO -> Home -> Create or Join group
 * -> Group/Vibe List -> Add activity or Start decision -> Vote rounds -> Winner
 * -> Plan details -> Memories.
 *
 * The bottom bar keeps Home, Memories and Profile one tap apart, and every back
 * press returns to the previous logical screen - a drafted offline action stays
 * because the draft lives in RoomDB, not in the back stack.
 */
object Routes {
    const val ONBOARDING = "onboarding"
    const val LOGIN = "login"
    const val REGISTER = "register"
    const val HOME = "home"
    const val GROUPS = "groups"
    const val CREATE_GROUP = "group/create"
    const val JOIN_GROUP = "group/join"
    const val GROUP_DETAIL = "group/{groupId}"
    const val ACTIVITIES = "activities/{groupId}"
    const val ACTIVITY_NEW = "activity/new/{groupId}"
    const val ACTIVITY_DETAIL = "activity/{activityId}"
    const val ACTIVITY_EDIT = "activity/edit/{activityId}"
    const val DECISION_START = "decision/start/{groupId}"
    const val DECISION_VOTE = "decision/vote/{decisionId}"
    const val DECISION_WINNER = "decision/winner/{groupId}"
    const val SURPRISE = "decision/surprise/{groupId}"
    const val PLAN = "plan/{activityId}"
    const val MEMORIES = "memories"
    const val MEMORY_DETAIL = "memory/{memoryId}"
    const val NOTIFICATIONS = "notifications"
    const val PROFILE = "profile"
    const val EDIT_PROFILE = "profile/edit"
    const val SETTINGS = "settings"
    const val LANGUAGE = "settings/language"
    const val PASSWORD = "settings/password"
    const val HELP = "help"

    const val ALL_GROUPS = "all"

    fun groupDetail(groupId: String) = "group/$groupId"
    fun activities(groupId: String?) = "activities/${groupId ?: ALL_GROUPS}"
    fun activityNew(groupId: String) = "activity/new/$groupId"
    fun activityDetail(activityId: String) = "activity/$activityId"
    fun activityEdit(activityId: String) = "activity/edit/$activityId"
    fun decisionStart(groupId: String) = "decision/start/$groupId"
    fun decisionVote(decisionId: String) = "decision/vote/$decisionId"
    fun decisionWinner(groupId: String) = "decision/winner/$groupId"
    fun surprise(groupId: String) = "decision/surprise/$groupId"
    fun plan(activityId: String) = "plan/$activityId"
    fun memoryDetail(memoryId: String) = "memory/$memoryId"
}

@Composable
fun VibeNavHost(
    navController: NavHostController,
    startDestination: String,
) {
    val toHome = {
        navController.navigate(Routes.HOME) {
            popUpTo(0) { inclusive = true }
        }
    }
    val toLogin = {
        navController.navigate(Routes.LOGIN) {
            popUpTo(0) { inclusive = true }
        }
    }

    NavHost(navController = navController, startDestination = startDestination) {

        composable(Routes.ONBOARDING) {
            OnboardingScreen(onGetStarted = { navController.navigate(Routes.LOGIN) })
        }
        composable(Routes.LOGIN) {
            LoginScreen(onSignedIn = toHome, onGoToRegister = { navController.navigate(Routes.REGISTER) })
        }
        composable(Routes.REGISTER) {
            RegisterScreen(onSignedIn = toHome, onGoToLogin = { navController.navigate(Routes.LOGIN) })
        }

        composable(Routes.HOME) {
            HomeScreen(
                onOpenGroup = { navController.navigate(Routes.groupDetail(it)) },
                onOpenGroups = { navController.navigate(Routes.GROUPS) },
                onOpenActivities = { navController.navigate(Routes.activities(null)) },
                onOpenNotifications = { navController.navigate(Routes.NOTIFICATIONS) },
                onCreateGroup = { navController.navigate(Routes.CREATE_GROUP) },
                onJoinGroup = { navController.navigate(Routes.JOIN_GROUP) },
                onStartDecision = { navController.navigate(Routes.decisionStart(it)) },
                onDecideForUs = { navController.navigate(Routes.activities(null)) },
                onSurpriseMe = { navController.navigate(Routes.surprise(it)) },
                onMemories = { navController.navigate(Routes.MEMORIES) },
                onProfile = { navController.navigate(Routes.PROFILE) },
            )
        }

        composable(Routes.GROUPS) {
            GroupsScreen(
                onOpenGroup = { navController.navigate(Routes.groupDetail(it)) },
                onBack = { navController.popBackStack() },
                onCreateGroup = { navController.navigate(Routes.CREATE_GROUP) },
                onJoinGroup = { navController.navigate(Routes.JOIN_GROUP) },
            )
        }
        composable(Routes.CREATE_GROUP) {
            CreateGroupScreen(
                onBack = { navController.popBackStack() },
                onOpenGroup = { navController.navigate(Routes.groupDetail(it)) },
            )
        }
        composable(Routes.JOIN_GROUP) {
            JoinGroupScreen(
                onBack = { navController.popBackStack() },
                onJoined = { navController.navigate(Routes.groupDetail(it)) },
            )
        }
        composable(
            route = Routes.GROUP_DETAIL,
            arguments = listOf(navArgument("groupId") { type = NavType.StringType }),
        ) { entry ->
            val groupId = entry.arguments?.getString("groupId").orEmpty()
            GroupDetailScreen(
                groupId = groupId,
                onBack = { navController.popBackStack() },
                onActivity = { navController.navigate(Routes.activityDetail(it)) },
                onAddActivity = { navController.navigate(Routes.activityNew(it)) },
                onVote = { navController.navigate(Routes.decisionVote(it)) },
                onPlan = { navController.navigate(Routes.plan(it)) },
            )
        }

        composable(
            route = Routes.ACTIVITIES,
            arguments = listOf(navArgument("groupId") { type = NavType.StringType }),
        ) { entry ->
            val raw = entry.arguments?.getString("groupId")
            val groupId = raw?.takeUnless { it == Routes.ALL_GROUPS }
            ActivitiesScreen(
                groupId = groupId,
                onBack = { navController.popBackStack() },
                onOpenActivity = { navController.navigate(Routes.activityDetail(it)) },
                onAddActivity = {
                    navController.navigate(Routes.activityNew(groupId ?: Routes.ALL_GROUPS))
                },
            )
        }
        composable(
            route = Routes.ACTIVITY_NEW,
            arguments = listOf(navArgument("groupId") { type = NavType.StringType }),
        ) { entry ->
            val groupId = entry.arguments?.getString("groupId").orEmpty()
            ActivityEditScreen(
                groupId = groupId,
                activityId = null,
                onBack = { navController.popBackStack() },
                onSaved = { navController.popBackStack() },
            )
        }
        composable(
            route = Routes.ACTIVITY_DETAIL,
            arguments = listOf(navArgument("activityId") { type = NavType.StringType }),
        ) { entry ->
            val activityId = entry.arguments?.getString("activityId").orEmpty()
            ActivityDetailScreen(
                activityId = activityId,
                onBack = { navController.popBackStack() },
                onPlan = { navController.navigate(Routes.plan(it)) },
                onEdit = { navController.navigate(Routes.activityEdit(it)) },
                onDeleted = { navController.popBackStack() },
            )
        }
        composable(
            route = Routes.ACTIVITY_EDIT,
            arguments = listOf(navArgument("activityId") { type = NavType.StringType }),
        ) { entry ->
            val activityId = entry.arguments?.getString("activityId").orEmpty()
            ActivityEditScreen(
                groupId = Routes.ALL_GROUPS,
                activityId = activityId,
                onBack = { navController.popBackStack() },
                onSaved = { navController.popBackStack() },
            )
        }

        composable(
            route = Routes.DECISION_START,
            arguments = listOf(navArgument("groupId") { type = NavType.StringType }),
        ) { entry ->
            val groupId = entry.arguments?.getString("groupId").orEmpty()
            DecisionStartScreen(
                groupId = groupId,
                onBack = { navController.popBackStack() },
                onStarted = { decisionId -> navController.navigate(Routes.decisionVote(decisionId)) },
                onAddActivity = { navController.navigate(Routes.activityNew(groupId)) },
            )
        }
        composable(
            route = Routes.DECISION_VOTE,
            arguments = listOf(navArgument("decisionId") { type = NavType.StringType }),
        ) { entry ->
            val decisionId = entry.arguments?.getString("decisionId").orEmpty()
            VoteScreen(
                decisionId = decisionId,
                onBack = { navController.popBackStack() },
                onFinished = { groupId -> navController.navigate(Routes.decisionWinner(groupId)) },
                onAddActivity = { navController.navigate(Routes.activities(null)) },
            )
        }
        composable(
            route = Routes.DECISION_WINNER,
            arguments = listOf(navArgument("groupId") { type = NavType.StringType }),
        ) { entry ->
            val groupId = entry.arguments?.getString("groupId").orEmpty()
            WinnerScreen(
                groupId = groupId,
                onBack = { navController.popBackStack() },
                onPlanIt = { navController.navigate(Routes.plan(it)) },
            )
        }
        composable(
            route = Routes.SURPRISE,
            arguments = listOf(navArgument("groupId") { type = NavType.StringType }),
        ) { entry ->
            val groupId = entry.arguments?.getString("groupId").orEmpty()
            SurpriseScreen(
                groupId = groupId,
                onBack = { navController.popBackStack() },
                onPlanned = { navController.navigate(Routes.plan(it)) },
                onAddActivity = { navController.navigate(Routes.activityNew(groupId)) },
            )
        }

        composable(
            route = Routes.PLAN,
            arguments = listOf(navArgument("activityId") { type = NavType.StringType }),
        ) { entry ->
            val activityId = entry.arguments?.getString("activityId").orEmpty()
            PlanScreen(
                activityId = activityId,
                onBack = { navController.popBackStack() },
                onSaved = { navController.navigate(Routes.MEMORIES) },
            )
        }
        composable(Routes.MEMORIES) {
            MemoriesScreen(
                onBack = { navController.popBackStack() },
                onOpenMemory = { navController.navigate(Routes.memoryDetail(it)) },
                onHome = toHome,
                onProfile = { navController.navigate(Routes.PROFILE) },
            )
        }
        composable(
            route = Routes.MEMORY_DETAIL,
            arguments = listOf(navArgument("memoryId") { type = NavType.StringType }),
        ) { entry ->
            val memoryId = entry.arguments?.getString("memoryId").orEmpty()
            MemoryDetailScreen(memoryId = memoryId, onBack = { navController.popBackStack() })
        }

        composable(Routes.NOTIFICATIONS) {
            NotificationsScreen(onBack = { navController.popBackStack() })
        }

        composable(Routes.PROFILE) {
            ProfileScreen(
                onBack = { navController.popBackStack() },
                onEditProfile = { navController.navigate(Routes.EDIT_PROFILE) },
                onMyGroups = { navController.navigate(Routes.GROUPS) },
                onMyActivities = { navController.navigate(Routes.activities(null)) },
                onSettings = { navController.navigate(Routes.SETTINGS) },
                onLanguage = { navController.navigate(Routes.LANGUAGE) },
                onHelp = { navController.navigate(Routes.HELP) },
                onHome = toHome,
                onMemories = { navController.navigate(Routes.MEMORIES) },
            )
        }
        composable(Routes.EDIT_PROFILE) {
            EditProfileScreen(onBack = { navController.popBackStack() })
        }
        composable(Routes.SETTINGS) {
            SettingsScreen(
                onBack = { navController.popBackStack() },
                onLanguage = { navController.navigate(Routes.LANGUAGE) },
                onChangePassword = { navController.navigate(Routes.PASSWORD) },
                onSignedOut = toLogin,
            )
        }
        composable(Routes.LANGUAGE) {
            LanguageScreen(onBack = { navController.popBackStack() })
        }
        composable(Routes.PASSWORD) {
            ChangePasswordScreen(onBack = { navController.popBackStack() })
        }
        composable(Routes.HELP) {
            HelpScreen(onBack = { navController.popBackStack() })
        }
    }
}
