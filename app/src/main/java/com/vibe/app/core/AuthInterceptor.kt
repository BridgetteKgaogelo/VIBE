package com.vibe.app.core

import okhttp3.Interceptor
import okhttp3.Response

/**
 * Adds the bearer token to every API call.
 *
 * The token is read from DataStore on each request rather than cached in memory,
 * so a sign-out (or a rotated token after refresh) takes effect immediately. The
 * token is never written to a log: [VibeContainer] redacts the Authorization
 * header on the logging interceptor.
 */
class AuthInterceptor(private val accessToken: suspend () -> String?) : Interceptor {

    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()
        if (request.header("Authorization") != null) return chain.proceed(request)

        val token = kotlinx.coroutines.runBlocking { accessToken() }
        val authorised = if (token.isNullOrBlank()) {
            request
        } else {
            request.newBuilder()
                .header("Authorization", "Bearer $token")
                .header("Accept", "application/json")
                .build()
        }
        return chain.proceed(authorised)
    }
}
