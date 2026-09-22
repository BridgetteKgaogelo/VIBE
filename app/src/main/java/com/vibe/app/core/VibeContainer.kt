package com.vibe.app.core

import android.content.Context
import androidx.room.Room
import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import com.vibe.app.BuildConfig
import com.vibe.app.data.local.VibeDatabase
import com.vibe.app.data.prefs.SettingsStore
import com.vibe.app.data.remote.DemoVibeApi
import com.vibe.app.data.remote.VibeApi
import com.vibe.app.data.repository.ActivityRepository
import com.vibe.app.data.repository.AuthRepository
import com.vibe.app.data.repository.DecisionRepository
import com.vibe.app.data.repository.GroupRepository
import com.vibe.app.data.repository.MemoryRepository
import com.vibe.app.data.repository.NotificationRepository
import com.vibe.app.data.sync.SyncManager
import com.vibe.app.data.sync.SyncQueue
import com.vibe.app.identity.GoogleSignInGateway
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.moshi.MoshiConverterFactory
import java.util.concurrent.TimeUnit

/**
 * Hand-rolled dependency container.
 *
 * The PoE needs a clear separation between the local cache, the remote API and
 * the presentation layer, and it needs to be obvious which implementation is
 * live. There is exactly one place where that choice is made - here - and
 * [BuildConfig.USE_DEMO_BACKEND] switches between the bundled demo backend and
 * the deployed ASP.NET Core service. No third-party DI framework is needed for a
 * project this size.
 */
class VibeContainer(private val context: Context) {

    val time: TimeProvider = SystemTime

    val appScope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    val database: VibeDatabase = Room.databaseBuilder(
        context.applicationContext,
        VibeDatabase::class.java,
        "vibe.db",
    ).fallbackToDestructiveMigration().build()

    val settings = SettingsStore(context)

    val connectivity: ConnectivityObserver = AndroidConnectivityObserver(context)

    private val moshi: Moshi = Moshi.Builder()
        .add(KotlinJsonAdapterFactory())
        .build()

    private val authInterceptor = AuthInterceptor { settings.currentAccessToken() }

    /** The REST client is HTTPS-only: cleartext is refused by the network config. */
    private fun retrofitApi(): VibeApi {
        val logging = HttpLoggingInterceptor().apply {
            level = if (BuildConfig.DEBUG) {
                HttpLoggingInterceptor.Level.BASIC
            } else {
                HttpLoggingInterceptor.Level.NONE
            }
            redactHeader("Authorization")
        }
        val client = OkHttpClient.Builder()
            .addInterceptor(authInterceptor)
            .addInterceptor(logging)
            .connectTimeout(20, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .writeTimeout(30, TimeUnit.SECONDS)
            .build()

        return Retrofit.Builder()
            .baseUrl(BuildConfig.API_BASE_URL)
            .client(client)
            .addConverterFactory(MoshiConverterFactory.create(moshi))
            .build()
            .create(VibeApi::class.java)
    }

    val api: VibeApi = if (BuildConfig.USE_DEMO_BACKEND) {
        DemoVibeApi(
            db = database,
            time = time,
            scope = appScope,
            currentUserId = { settings.currentUserId() },
        )
    } else {
        retrofitApi()
    }

    val syncQueue = SyncQueue(database, moshi, time)

    val syncManager = SyncManager(api, database, connectivity, appScope)

    val authRepository = AuthRepository(api, database, settings, syncQueue, time)

    val groupRepository = GroupRepository(api, database, syncQueue, settings, time)

    val activityRepository = ActivityRepository(api, database, syncQueue, settings, time)

    val decisionRepository = DecisionRepository(api, database, syncQueue, settings, time)

    val memoryRepository = MemoryRepository(api, database, syncQueue, settings, time)

    val notificationRepository = NotificationRepository(api, database, settings, time)

    val googleSignIn: GoogleSignInGateway = GoogleSignInGateway(context)

    /** Called from [com.vibe.app.VibeApplication]. */
    fun start() {
        syncManager.start()
    }

    companion object {
        @Volatile
        private var instance: VibeContainer? = null

        /**
         * One container per process: Room, DataStore, the HTTP client and the
         * sync queue are all singletons, so the application and the activity must
         * receive the same instance.
         */
        fun provide(context: Context): VibeContainer =
            instance ?: synchronized(this) {
                instance ?: VibeContainer(context.applicationContext).also {
                    it.start()
                    instance = it
                }
            }
    }
}
