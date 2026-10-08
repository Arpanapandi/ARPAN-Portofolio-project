<?php

namespace App\Http\Controllers\Auth;

use App\Http\Controllers\Controller;
use App\Models\User;
use Illuminate\Auth\Events\Registered;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\Hash;
use Illuminate\Validation\Rules;

class RegisteredUserController extends Controller
{
    /**
     * Tampilkan form register
     */
    public function create()
    {
        return view('auth.register');
    }

    /**
     * Handle register baru
     */
    public function store(Request $request)
    {
        // Validasi input
        $request->validate([
            'name' => ['required', 'string', 'max:255'],
            'email' => ['required', 'string', 'email', 'max:255', 'unique:users'],
            'password' => ['required', 'confirmed', Rules\Password::defaults()],
        ]);

        $user = User::create([
            'name' => $request->name,      // sesuaikan dengan kolom database
            'email' => $request->email,
            'password' => Hash::make($request->password),
            'role' => 'user',               // kolom role harus ada
        ]);


        // Event registered
        event(new Registered($user));

        // Login otomatis
        Auth::login($user);

        // Redirect sesuai role
        return redirect()->route('user.dashboard');
    }
}
